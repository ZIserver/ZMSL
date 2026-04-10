using ZMSL.Shared.DTOs;

namespace ZMSL.App.Services;

public class AuthService
{
    private readonly ApiService _apiService;
    private readonly DatabaseService _db;

    public UserDto? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser != null;
    public event EventHandler? LoginStateChanged;

    public AuthService(ApiService apiService, DatabaseService db)
    {
        _apiService = apiService;
        _db = db;
        _ = TryAutoLoginAsync();
    }

    private async Task TryAutoLoginAsync()
    {
        var settings = await _db.GetSettingsAsync();
        if (!string.IsNullOrEmpty(settings.UserToken))
        {
            _apiService.SetToken(settings.UserToken);
            var result = await _apiService.GetCurrentUserAsync();
            if (result.Success && result.Data != null)
            {
                CurrentUser = result.Data;
                LoginStateChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // Token无效，清除
                settings.UserToken = null;
                await _db.SaveSettingsAsync(settings);
                _apiService.SetToken(null);
            }
        }
    }

    public async Task<(bool Success, string? Message)> LoginAsync(string username, string password, bool rememberMe = false)
    {
        var result = await _apiService.LoginAsync(new LoginRequest
        {
            Username = username,
            Password = password
        });

        if (result.Success && result.Token != null && result.User != null)
        {
            CurrentUser = result.User;
            _apiService.SetToken(result.Token);

            // 保存Token
            var settings = await _db.GetSettingsAsync();
            // 只要登录成功就保存 Token，实现自动登录
            // 如果需要记住我功能，可以在这里加判断，但目前需求似乎是默认记住
            settings.UserToken = result.Token;
            await _db.SaveSettingsAsync(settings);

            LoginStateChanged?.Invoke(this, EventArgs.Empty);
            return (true, null);
        }

        return (false, result.Message ?? "登录失败");
    }

    public async Task<(bool Success, string? Message)> RegisterAsync(string username, string email, string password)
    {
        var result = await _apiService.RegisterAsync(new RegisterRequest
        {
            Username = username,
            Email = email,
            Password = password
        });

        return (result.Success, result.Message);
    }

    public async void Logout()
    {
        CurrentUser = null;
        _apiService.SetToken(null);

        var settings = await _db.GetSettingsAsync();
        settings.UserToken = null;
        await _db.SaveSettingsAsync(settings);

        LoginStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshUserInfoAsync()
    {
        if (!IsLoggedIn) return;

        var result = await _apiService.GetCurrentUserAsync();
        if (result.Success && result.Data != null)
        {
            CurrentUser = result.Data;
        }
    }
}
