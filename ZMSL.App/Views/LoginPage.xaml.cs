using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views;

public sealed partial class LoginPage : Page
{
    private readonly LoginViewModel _viewModel;

    public LoginPage()
    {
        this.InitializeComponent();
        _viewModel = App.Services.GetRequiredService<LoginViewModel>();
        _viewModel.LoginSuccess += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                Frame.GoBack();
            });
        };
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Username = UsernameBox.Text;
        _viewModel.Password = PasswordBox.Password;

        LoadingRing.IsActive = true;
        await _viewModel.LoginCommand.ExecuteAsync(null);
        LoadingRing.IsActive = false;

        ShowMessage();
    }

    private void ShowMessage()
    {
        if (!string.IsNullOrEmpty(_viewModel.ErrorMessage))
        {
            MessageBar.Severity = InfoBarSeverity.Error;
            MessageBar.Message = _viewModel.ErrorMessage;
            MessageBar.IsOpen = true;
        }
        else if (!string.IsNullOrEmpty(_viewModel.SuccessMessage))
        {
            MessageBar.Severity = InfoBarSeverity.Success;
            MessageBar.Message = _viewModel.SuccessMessage;
            MessageBar.IsOpen = true;
        }
        else
        {
            MessageBar.IsOpen = false;
        }
    }
}
