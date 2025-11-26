using System;
using System.IO;
using System.Text.Json;
using auto_creamapi.Utils;

namespace auto_creamapi.Services
{
    public interface IConfigService
    {
        string GetSteamApiKey();
        void SetSteamApiKey(string apiKey);
        bool HasSteamApiKey();
    }

    public class ConfigService : IConfigService
    {
        private const string ConfigFileName = "config.json";
        private AppConfig _config;

        public ConfigService()
        {
            LoadConfig();
        }

        public string GetSteamApiKey()
        {
            return _config?.SteamApiKey ?? string.Empty;
        }

        public void SetSteamApiKey(string apiKey)
        {
            _config ??= new AppConfig();
            _config.SteamApiKey = apiKey;
            SaveConfig();
        }

        public bool HasSteamApiKey()
        {
            return !string.IsNullOrWhiteSpace(_config?.SteamApiKey);
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFileName))
                {
                    var json = File.ReadAllText(ConfigFileName);
                    _config = JsonSerializer.Deserialize<AppConfig>(json);
                    MyLogger.Log.Information("Configuration loaded successfully");
                }
                else
                {
                    _config = new AppConfig();
                    MyLogger.Log.Information("No configuration file found, using defaults");
                }
            }
            catch (Exception ex)
            {
                MyLogger.Log.Error(ex, "Failed to load configuration, using defaults");
                _config = new AppConfig();
            }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(ConfigFileName, json);
                MyLogger.Log.Information("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                MyLogger.Log.Error(ex, "Failed to save configuration");
            }
        }

        private class AppConfig
        {
            public string SteamApiKey { get; set; } = string.Empty;
        }
    }
}