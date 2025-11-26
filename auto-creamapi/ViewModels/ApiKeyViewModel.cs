using System.Diagnostics;
using auto_creamapi.Services;
using Microsoft.Extensions.Logging;
using MvvmCross.Commands;
using MvvmCross.Navigation;
using MvvmCross.ViewModels;

namespace auto_creamapi.ViewModels
{
    public class ApiKeyViewModel : MvxNavigationViewModel<bool, bool>
    {
        private readonly IConfigService _configService;
        private readonly ILogger<ApiKeyViewModel> _logger;
        private string _apiKey;
        private bool _isFirstRun;

        public ApiKeyViewModel(
            ILoggerFactory loggerFactory, 
            IMvxNavigationService navigationService,
            IConfigService configService) 
            : base(loggerFactory, navigationService)
        {
            _logger = loggerFactory.CreateLogger<ApiKeyViewModel>();
            _configService = configService;
        }

        public string ApiKey
        {
            get => _apiKey;
            set
            {
                _apiKey = value;
                RaisePropertyChanged(() => ApiKey);
                RaisePropertyChanged(() => CanSave);
            }
        }

        public bool IsFirstRun
        {
            get => _isFirstRun;
            set
            {
                _isFirstRun = value;
                RaisePropertyChanged(() => IsFirstRun);
                RaisePropertyChanged(() => WindowTitle);
                RaisePropertyChanged(() => CanCancel);
            }
        }

        public string WindowTitle => IsFirstRun 
            ? "Auto-CreamAPI 2: Steam API Key Required" 
            : "Auto-CreamAPI 2: Steam API Key Settings";

        public bool CanSave => !string.IsNullOrWhiteSpace(ApiKey);

        public bool CanCancel => !IsFirstRun;

        public IMvxCommand SaveCommand => new MvxAsyncCommand(Save, () => CanSave);
        public IMvxCommand CancelCommand => new MvxAsyncCommand(Cancel, () => CanCancel);
        public IMvxCommand OpenWebsiteCommand => new MvxCommand(OpenWebsite);

        public override void Prepare(bool isFirstRun)
        {
            IsFirstRun = isFirstRun;
            if (!isFirstRun)
            {
                ApiKey = _configService.GetSteamApiKey();
            }
        }

        private async System.Threading.Tasks.Task Save()
        {
            _configService.SetSteamApiKey(ApiKey);
            _logger.LogInformation("Steam API key saved");
            await NavigationService.Close(this, true);
        }

        private async System.Threading.Tasks.Task Cancel()
        {
            await NavigationService.Close(this, false);
        }

        private void OpenWebsite()
        {
            var uri = new System.Uri("https://steamcommunity.com/dev/apikey");
            var process = new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            };
            Process.Start(process);
        }
    }
}