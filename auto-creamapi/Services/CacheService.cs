using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using auto_creamapi.Models;
using auto_creamapi.Utils;
using NinjaNye.SearchExtensions;
using SteamStorefrontAPI;

namespace auto_creamapi.Services
{
    public interface ICacheService
    {
        public Task Initialize();
        public IEnumerable<SteamApp> GetListOfAppsByName(string name);
        public SteamApp GetAppByName(string name);
        public SteamApp GetAppById(int appid);
        public Task<List<SteamApp>> GetListOfDlc(SteamApp steamApp, bool useSteamDb, bool ignoreUnknown);
    }

    public class CacheService : ICacheService
    {
        private const string CachePath = "steamapps.json";
        private const string SteamUri = "https://api.steampowered.com/IStoreService/GetAppList/v1/";
        private readonly IConfigService _configService;

        private HashSet<SteamApp> _cache = [];

        public CacheService(IConfigService configService)
        {
            _configService = configService;
        }

        public async Task Initialize()
        {
            MyLogger.Log.Information("Updating cache...");
            var updateNeeded = !File.Exists(CachePath) || 
                             DateTime.Now.Subtract(File.GetLastWriteTimeUtc(CachePath)).TotalDays >= 1;
            string cacheString;
            if (updateNeeded)
            {
                cacheString = await UpdateCache().ConfigureAwait(false);
            }
            else
            {
                MyLogger.Log.Information("Cache already up to date!");
                cacheString = File.ReadAllText(CachePath);
            }

            // Validate that we have valid JSON before trying to deserialize
            if (string.IsNullOrWhiteSpace(cacheString) || cacheString.TrimStart().StartsWith("<"))
            {
                MyLogger.Log.Error("Cache content is not valid JSON. Content starts with: {Preview}", 
                    cacheString.Length > 100 ? cacheString.Substring(0, 100) : cacheString);
                
                if (File.Exists(CachePath))
                {
                    MyLogger.Log.Warning("Attempting to use existing cache file despite age...");
                    cacheString = File.ReadAllText(CachePath);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Failed to retrieve valid Steam app list from API and no cached version exists. " +
                        "Please check your internet connection and Steam API key, then try again.");
                }
            }

            // Parse the API response format
            var response = JsonSerializer.Deserialize<JsonDocument>(cacheString);
            var apps = response.RootElement.GetProperty("response").GetProperty("apps");
            
            var appList = new List<SteamApp>();
            foreach (var app in apps.EnumerateArray())
            {
                if (app.TryGetProperty("appid", out var appIdElement) && 
                    app.TryGetProperty("name", out var nameElement))
                {
                    appList.Add(new SteamApp
                    {
                        AppId = appIdElement.GetInt32(),
                        Name = nameElement.GetString()
                    });
                }
            }
            
            _cache = new HashSet<SteamApp>(appList);
            MyLogger.Log.Information("Loaded {Count} apps into cache!", _cache.Count);
        }

        private async Task<string> UpdateCache()
        {
            MyLogger.Log.Information("Getting content from API...");
            
            var apiKey = _configService.GetSteamApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException(
                    "Steam API key is not configured. Please set your Steam API key in the settings. " +
                    "You can get one from: https://steamcommunity.com/dev/apikey");
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "auto-creamapi");
            client.Timeout = TimeSpan.FromSeconds(60);

            var allApps = new List<SteamApp>();
            int lastAppId = 0;
            const int maxResults = 50000;
            bool hasMoreResults = true;

            try
            {
                while (hasMoreResults)
                {
                    var url = $"{SteamUri}?key={apiKey}&max_results={maxResults}";
                    if (lastAppId > 0)
                    {
                        url += $"&last_appid={lastAppId}";
                    }
                    
                    url += "&include_games=true&include_dlc=true&include_software=true&include_videos=true&include_hardware=true";

                    MyLogger.Log.Debug("Fetching page starting at appid {LastAppId}...", lastAppId);
                    
                    var response = await client.GetAsync(url).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    
                    if (string.IsNullOrWhiteSpace(responseBody) || responseBody.TrimStart().StartsWith("<"))
                    {
                        MyLogger.Log.Error("API returned non-JSON content. Response preview: {Preview}", 
                            responseBody.Length > 200 ? responseBody.Substring(0, 200) : responseBody);
                        throw new InvalidOperationException("Steam API returned HTML instead of JSON");
                    }

                    var jsonDoc = JsonSerializer.Deserialize<JsonDocument>(responseBody);
                    
                    if (!jsonDoc.RootElement.TryGetProperty("response", out var responseElement))
                    {
                        MyLogger.Log.Error("Response does not contain 'response' property");
                        break;
                    }

                    if (!responseElement.TryGetProperty("apps", out var apps))
                    {
                        MyLogger.Log.Error("Response does not contain 'apps' property");
                        break;
                    }

                    var haveMoreResults = responseElement.TryGetProperty("have_more_results", out var moreResultsElement) 
                        && moreResultsElement.GetBoolean();
                    
                    int count = 0;
                    foreach (var app in apps.EnumerateArray())
                    {
                        if (!app.TryGetProperty("appid", out var appIdElement) || 
                            !app.TryGetProperty("name", out var nameElement))
                        {
                            MyLogger.Log.Warning("Skipping app with missing appid or name");
                            continue;
                        }

                        var appId = appIdElement.GetInt32();
                        var name = nameElement.GetString();
                        allApps.Add(new SteamApp { AppId = appId, Name = name });
                        lastAppId = appId;
                        count++;
                    }

                    MyLogger.Log.Information("Retrieved {Count} apps (total so far: {Total})", count, allApps.Count);
                    
                    if (count == 0 || !haveMoreResults)
                    {
                        hasMoreResults = false;
                    }
                }

                MyLogger.Log.Information("Got content from API successfully. Writing to file...");

                var cacheData = new
                {
                    response = new
                    {
                        apps = allApps.Select(a => new { appid = a.AppId, name = a.Name }).ToList()
                    }
                };

                var cacheString = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });

                await File.WriteAllTextAsync(CachePath, cacheString, Encoding.UTF8).ConfigureAwait(false);
                MyLogger.Log.Information("Cache written to file successfully.");
                
                return cacheString;
            }
            catch (HttpRequestException ex)
            {
                MyLogger.Log.Error(ex, "HTTP request failed while updating cache");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                MyLogger.Log.Error(ex, "Request timed out while updating cache");
                throw;
            }
        }

        public IEnumerable<SteamApp> GetListOfAppsByName(string name)
        {
            var listOfAppsByName = _cache.Search(x => x.Name)
                .SetCulture(StringComparison.OrdinalIgnoreCase)
                .ContainingAll(name.Split(' '));
            return listOfAppsByName;
        }

        public SteamApp GetAppByName(string name)
        {
            MyLogger.Log.Information("Trying to get app {Name}", name);
            var comparableName = Regex.Replace(name, Misc.SpecialCharsRegex, "").ToLower();
            var app = _cache.FirstOrDefault(x => x.CompareName(comparableName));
            if (app != null) MyLogger.Log.Information("Successfully got app {App}", app);
            return app;
        }

        public SteamApp GetAppById(int appid)
        {
            MyLogger.Log.Information("Trying to get app with ID {AppId}", appid);
            var app = _cache.FirstOrDefault(x => x.AppId.Equals(appid));
            if (app != null) MyLogger.Log.Information("Successfully got app {App}", app);
            return app;
        }

        public async Task<List<SteamApp>> GetListOfDlc(SteamApp steamApp, bool useSteamDb, bool ignoreUnknown)
        {
            MyLogger.Log.Debug("Start: GetListOfDlc");
            var dlcList = new List<SteamApp>();
            try
            {
                if (steamApp != null)
                {
                    var steamAppDetails = await AppDetails.GetAsync(steamApp.AppId).ConfigureAwait(false);
                    if (steamAppDetails != null)
                    {
                        MyLogger.Log.Debug("Type for Steam App {Name}: \"{Type}\"", steamApp.Name,
                            steamAppDetails.Type);
                        if (steamAppDetails.Type == "game" || steamAppDetails.Type == "demo")
                        {
                            steamAppDetails.DLC.ForEach(x =>
                            {
                                var result = _cache.FirstOrDefault(y => y.AppId.Equals(x));
                                if (result == null) return;
                                var dlcDetails = AppDetails.GetAsync(x).Result;
                                dlcList.Add(dlcDetails != null
                                    ? new SteamApp { AppId = dlcDetails.SteamAppId, Name = dlcDetails.Name }
                                    : new SteamApp { AppId = x, Name = $"Unknown DLC {x}" });
                            });

                            dlcList.ForEach(x => MyLogger.Log.Debug("{AppId}={Name}", x.AppId, x.Name));
                            MyLogger.Log.Information("Got DLC successfully...");

                            if (!useSteamDb) return dlcList;

                            string steamDbUrl = $"https://steamdb.info/app/{steamApp.AppId}/dlc/";

                            var client = new HttpClient();
                            string archiveJson = await client.GetStringAsync($"https://archive.org/wayback/available?url={steamDbUrl}");
                            var archiveResult = JsonSerializer.Deserialize<AvailableArchive>(archiveJson);

                            if (archiveResult == null || archiveResult.ArchivedSnapshots.Closest?.Status != "200")
                            {
                                return dlcList;
                            }

                            const string pattern = @"^(https?:\/\/web\.archive\.org\/web\/\d+)(\/.+)$";
                            const string substitution = "$1id_$2";
                            const RegexOptions options = RegexOptions.Multiline;

                            Regex regex = new(pattern, options);
                            string newUrl = regex.Replace(archiveResult.ArchivedSnapshots.Closest.Url, substitution);

                            MyLogger.Log.Information("Get SteamDB App");
                            var httpCall = client.GetAsync(newUrl);
                            var response = await httpCall.ConfigureAwait(false);
                            MyLogger.Log.Debug("{Status}", httpCall.Status.ToString());
                            MyLogger.Log.Debug("{Boolean}", response.IsSuccessStatusCode.ToString());

                            response.EnsureSuccessStatusCode();

                            var readAsStringAsync = response.Content.ReadAsStringAsync();
                            var responseBody = await readAsStringAsync.ConfigureAwait(false);
                            MyLogger.Log.Debug("{Status}", readAsStringAsync.Status.ToString());

                            var parser = new HtmlParser();
                            var doc = parser.ParseDocument(responseBody);

                            var query1 = doc.QuerySelector("#dlc");
                            if (query1 != null)
                            {
                                var query2 = query1.QuerySelectorAll(".app");
                                foreach (var element in query2)
                                {
                                    var dlcId = element.GetAttribute("data-appid");
                                    var query3 = element.QuerySelectorAll("td");
                                    var dlcName = query3 == null
                                        ? $"Unknown DLC {dlcId}"
                                        : query3[1].Text().Replace("\n", "").Trim();

                                    if (ignoreUnknown && dlcName.Contains("SteamDB Unknown App"))
                                    {
                                        MyLogger.Log.Information("Skipping SteamDB Unknown App {DlcId}", dlcId);
                                    }
                                    else
                                    {
                                        var dlcApp = new SteamApp { AppId = Convert.ToInt32(dlcId), Name = dlcName };
                                        var i = dlcList.FindIndex(x => x.AppId.Equals(dlcApp.AppId));
                                        if (i > -1)
                                        {
                                            if (dlcList[i].Name.Contains("Unknown DLC")) dlcList[i] = dlcApp;
                                        }
                                        else
                                        {
                                            dlcList.Add(dlcApp);
                                        }
                                    }
                                }

                                dlcList.ForEach(x => MyLogger.Log.Debug("{AppId}={Name}", x.AppId, x.Name));
                                MyLogger.Log.Information("Got DLC from SteamDB successfully...");
                            }
                            else
                            {
                                MyLogger.Log.Error("Could not get DLC from SteamDB!");
                            }
                        }
                        else
                        {
                            MyLogger.Log.Error("Could not get DLC: Steam App is not of type: \"Game\"");
                        }
                    }
                    else
                    {
                        MyLogger.Log.Error("Could not get DLC: Could not get Steam App details");
                    }
                }
                else
                {
                    MyLogger.Log.Error("Could not get DLC: Invalid Steam App");
                }
            }
            catch (Exception e)
            {
                MyLogger.Log.Error("Could not get DLC!");
                MyLogger.Log.Debug(e.Demystify(), "Exception thrown!");
            }

            return dlcList;
        }
    }
}