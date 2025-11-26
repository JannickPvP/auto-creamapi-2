using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using auto_creamapi.Messenger;
using auto_creamapi.Utils;
using HttpProgress;
using MvvmCross.Plugin.Messenger;
using SevenZip;

namespace auto_creamapi.Services
{
    public interface IDownloadCreamApiService
    {
        public Task<string> Download();
        public Task Extract(string filename);
    }

    public class DownloadCreamApiService : IDownloadCreamApiService
    {
        private const string ArchivePassword = "cs.rin.ru";
        private const string DownloadUrl = "https://www.dropbox.com/scl/fi/m9e73w6fi5kzzt5tqa32e/CreamAPI_Release_v5.3.0.0.7z?rlkey=ecpufkebdl6idokow6b8qf8lz&st=pnwm5x35&dl=1";
        private const string Filename = "CreamAPI_Release_v5.3.0.0.7z";
        private readonly IMvxMessenger _messenger;

        public DownloadCreamApiService(IMvxMessenger messenger)
        {
            _messenger = messenger;
        }

        public async Task<string> Download()
        {
            MyLogger.Log.Debug("Download");
            
            if (File.Exists(Filename))
            {
                MyLogger.Log.Information("{Filename} already exists, skipping download...", Filename);
                return Filename;
            }

            MyLogger.Log.Information("Start download from {Url}...", DownloadUrl);
            
            try
            {
                var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:86.0) " +
                                                                "Gecko/20100101 Firefox/86.0");
                
                var progress = new Progress<ICopyProgress>(
                    x => _messenger.Publish(new ProgressMessage(this, "Downloading...", Filename, x)));
                
                await using var fileStream = File.OpenWrite(Filename);
                var task = client.GetAsync(DownloadUrl, fileStream, progress);
                await task.ConfigureAwait(false);
                
                if (task.IsCompletedSuccessfully)
                    _messenger.Publish(new ProgressMessage(this, "Downloading...", Filename, 1.0));
                
                MyLogger.Log.Information("Download done.");
                return Filename;
            }
            catch (Exception ex)
            {
                MyLogger.Log.Error(ex, "Failed to download CreamAPI");
                if (File.Exists(Filename))
                    File.Delete(Filename);
                throw;
            }
        }

        public async Task Extract(string filename)
        {
            MyLogger.Log.Debug("Extract");
            var cwd = Directory.GetCurrentDirectory();
            const string nonlogBuild = "nonlog_build";
            const string windowsFolder = "windows";
            const string steamApi64Dll = "steam_api64.dll";
            const string steamApiDll = "steam_api.dll";
            
            MyLogger.Log.Information(@"Start extraction of ""{Filename}""...", filename);
            var nonlogBuildPath = Path.Combine(cwd, nonlogBuild);
            var windowsPath = Path.Combine(nonlogBuildPath, windowsFolder);
            
            if (Directory.Exists(nonlogBuildPath))
                Directory.Delete(nonlogBuildPath, true);
            
            _messenger.Publish(new ProgressMessage(this, "Extracting...", filename, 0.5));
            SevenZipBase.SetLibraryPath(Path.Combine(cwd, "resources/7z.dll"));
            
            using (var extractor =
                new SevenZipExtractor(filename, ArchivePassword, InArchiveFormat.SevenZip)
                    {PreserveDirectoryStructure = false})
            {
                await extractor.ExtractFilesAsync(
                    cwd,
                    $@"{nonlogBuild}\{windowsFolder}\{steamApi64Dll}",
                    $@"{nonlogBuild}\{windowsFolder}\{steamApiDll}"
                ).ConfigureAwait(false);
            }

            if (File.Exists(Path.Combine(windowsPath, steamApi64Dll)))
                File.Move(
                    Path.Combine(windowsPath, steamApi64Dll),
                    Path.Combine(cwd, steamApi64Dll),
                    true
                );

            if (File.Exists(Path.Combine(windowsPath, steamApiDll)))
                File.Move(
                    Path.Combine(windowsPath, steamApiDll),
                    Path.Combine(cwd, steamApiDll),
                    true
                );

            if (Directory.Exists(nonlogBuildPath))
                Directory.Delete(nonlogBuildPath, true);
            
            _messenger.Publish(new ProgressMessage(this, "Extracting...", filename, 1.0));
            MyLogger.Log.Information("Extraction done!");
        }
    }
}