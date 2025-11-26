using MvvmCross.Platforms.Wpf.Presenters.Attributes;

namespace auto_creamapi.Views
{
    [MvxWindowPresentation(Identifier = nameof(ApiKeyView), Modal = true)]
    public partial class ApiKeyView
    {
        public ApiKeyView()
        {
            InitializeComponent();
        }
    }
}