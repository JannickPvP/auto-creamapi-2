using System.Diagnostics;
using auto_creamapi.Models;
using auto_creamapi.Services;
using Microsoft.Extensions.Logging;
using MvvmCross.Commands;
using MvvmCross.Navigation;
using MvvmCross.ViewModels;

namespace auto_creamapi.ViewModels
{
    public class ApiKeyViewModel : MvxNavigationViewModel<ApiKeyNavigationParameter, ApiKeyNavigationResult>
    {
        private readonly IConfigService _configService;
        private readonly ILogger<ApiKeyViewModel> _logger;
        private string _apiKey = string.Empty;
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
                _saveCommand?.RaiseCanExecuteChanged();
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

        private IMvxAsyncCommand _saveCommand;
        private IMvxAsyncCommand _cancelCommand;

        public IMvxCommand SaveCommand => _saveCommand ??= new MvxAsyncCommand(Save, () => CanSave);
        public IMvxCommand CancelCommand => _cancelCommand ??= new MvxAsyncCommand(Cancel, () => CanCancel);
        public IMvxCommand OpenWebsiteCommand => new MvxCommand(OpenWebsite);

        public override void Prepare(ApiKeyNavigationParameter parameter)
        {
            IsFirstRun = parameter?.IsFirstRun ?? false;
            if (!IsFirstRun)
            {
                ApiKey = _configService.GetSteamApiKey();
            }
        }

        private async System.Threading.Tasks.Task Save()
        {
            _configService.SetSteamApiKey(ApiKey);
            _logger.LogInformation("Steam API key saved");
            await NavigationService.Close(this, new ApiKeyNavigationResult { Success = true });
        }

        private async System.Threading.Tasks.Task Cancel()
        {
            await NavigationService.Close(this, new ApiKeyNavigationResult { Success = false });
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