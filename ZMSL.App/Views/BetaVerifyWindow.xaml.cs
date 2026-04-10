using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Services;
using System;
using System.Threading.Tasks;
using Windows.Security.ExchangeActiveSyncProvisioning;

namespace ZMSL.App.Views;

public sealed partial class BetaVerifyWindow : Window
{
    private readonly ApiService _apiService;
    
    public BetaVerifyWindow()
    {
        this.InitializeComponent();
        _apiService = App.Services.GetRequiredService<ApiService>();
        
        // 设置窗口属性
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        // 显示 HWID
        HwidText.Text = $"Device ID: {GetHwid()}";
        
        // 尝试自动填充并验证
        _ = CheckSavedKeyAsync();
    }

    private async Task CheckSavedKeyAsync()
    {
        try
        {
            // 优先从文件读取 Key (因为 LocalSettings 在该机器上不稳定)
            var appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZMSL");
            var keyFilePath = System.IO.Path.Combine(appDataPath, "beta_key.txt");

            string savedKey = "";
            
            if (System.IO.File.Exists(keyFilePath))
            {
                savedKey = await System.IO.File.ReadAllTextAsync(keyFilePath);
                savedKey = savedKey.Trim();
            }
            // 兼容旧的 LocalSettings 读取
            else 
            {
                try 
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.TryGetValue("BetaKey", out var savedKeyObj) && savedKeyObj is string oldSavedKey)
                    {
                        savedKey = oldSavedKey;
                    }
                }
                catch {}
            }

            if (!string.IsNullOrWhiteSpace(savedKey))
            {
                // 切换到 UI 线程更新 UI
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    KeyBox.Text = savedKey;
                });
                
                // 自动验证
                await VerifyKeyAsync(savedKey, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BetaVerify] 读取保存的 Key 失败: {ex.Message}");
        }
    }

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        await VerifyKeyAsync(KeyBox.Text, false);
    }

    private async Task VerifyKeyAsync(string key, bool isAuto)
    {
        // 确保在 UI 线程上
        if (!this.DispatcherQueue.HasThreadAccess)
        {
            this.DispatcherQueue.TryEnqueue(() => _ = VerifyKeyAsync(key, isAuto));
            return;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            if (!isAuto) ShowError("请输入卡密");
            return;
        }

        SetLoading(true);
        try
        {
            var hwid = GetHwid();
            System.Diagnostics.Debug.WriteLine($"[BetaVerify] Verifying Key: {key}, HWID: {hwid}");

            // 增加一层保护，防止 ApiService 为空 (虽然不太可能)
            if (_apiService == null)
            {
                throw new InvalidOperationException("ApiService is not initialized");
            }

            var result = await _apiService.ValidateBetaKeyAsync(key, hwid);

            if (result.Success)
            {
                try
                {
                    // 保存 Key 到文件
                    var appDataPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ZMSL");
                    System.IO.Directory.CreateDirectory(appDataPath);
                    var keyFilePath = System.IO.Path.Combine(appDataPath, "beta_key.txt");
                    await System.IO.File.WriteAllTextAsync(keyFilePath, key);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BetaVerify] 保存 Key 失败: {ex.Message}");
                }

                // 启动主窗口
                var app = (App)Application.Current;
                app.LaunchMainWindow();
                
                // 关闭当前窗口
                this.Close();
            }
            else
            {
                // 如果是自动验证失败，不要报错，只是显示窗口让用户重新输入
                if (!isAuto)
                {
                    ShowError(result.Message ?? "验证失败，卡密无效或已过期");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BetaVerify] Error: {ex}");
            if (!isAuto) ShowError($"验证出错: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        VerifyButton.IsEnabled = !isLoading;
        KeyBox.IsEnabled = !isLoading;
        if (isLoading) ErrorText.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private string GetHwid()
    {
        // 优先使用文件持久化存储 HWID，确保稳定性
        try
        {
            var appDataPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZMSL");
            System.IO.Directory.CreateDirectory(appDataPath);
            var hwidFilePath = System.IO.Path.Combine(appDataPath, "device_id.txt");

            if (System.IO.File.Exists(hwidFilePath))
            {
                var savedId = System.IO.File.ReadAllText(hwidFilePath).Trim();
                if (!string.IsNullOrEmpty(savedId))
                {
                    return savedId;
                }
            }

            // 如果没有保存的 ID，生成一个新的
            // 尝试获取系统原有 ID 作为种子（可选）
            string newId;
            try
            {
                var device = new EasClientDeviceInformation();
                if (device.Id != Guid.Empty)
                {
                    newId = device.Id.ToString();
                }
                else
                {
                    newId = Guid.NewGuid().ToString();
                }
            }
            catch
            {
                newId = Guid.NewGuid().ToString();
            }

            // 保存到文件
            System.IO.File.WriteAllText(hwidFilePath, newId);
            return newId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BetaVerify] HWID File Error: {ex.Message}");
            // 最后的兜底，但内存中保持不变
            if (_tempHwid == null) _tempHwid = Guid.NewGuid().ToString();
            return _tempHwid;
        }
    }
    
    private static string? _tempHwid;
}
