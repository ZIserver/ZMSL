using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ZMSL.App.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly Services.AuthService _authService;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RememberMe { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SuccessMessage { get; set; } = string.Empty;

    public event EventHandler? LoginSuccess;

    public LoginViewModel(Services.AuthService authService)
    {
        _authService = authService;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "请输入用户名和密码";
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            var (success, message) = await _authService.LoginAsync(Username, Password, RememberMe);
            if (success)
            {
                SuccessMessage = "登录成功";
                LoginSuccess?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = message ?? "登录失败";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"登录失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
