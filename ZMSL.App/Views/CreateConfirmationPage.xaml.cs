using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class CreateConfirmationPage : Page
{
    private readonly ServerManagerService _serverManager;
    private readonly ServerDownloadService _downloadService;
    private LocalServer? _serverConfig;
    private bool _isCreating = false;

    public CreateConfirmationPage()
    {
        this.InitializeComponent();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.Parameter is LocalServer serverConfig)
        {
            _serverConfig = serverConfig;
            DisplayConfiguration();
        }
        else
        {
            Frame.GoBack();
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void DisplayConfiguration()
    {
        if (_serverConfig == null) return;

        ServerNameText.Text = _serverConfig.Name;
        ModeText.Text = _serverConfig.Mode == CreateMode.Beginner ? "小白模式" : "高手模式";
        PlayerCapacityText.Text = $"{_serverConfig.PlayerCapacity}人";
        
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZMSL", "servers");
        var serverPath = Path.Combine(baseDir, _serverConfig.Name);
        ServerPathText.Text = serverPath;
        _serverConfig.ServerPath = serverPath;

        if (_serverConfig.UseLatestPurpur && _serverConfig.Mode == CreateMode.Beginner)
        {
            CoreTypeText.Text = "Purpur";
            
            var displayVer = (_serverConfig.CoreVersion == "latest" || string.IsNullOrEmpty(_serverConfig.CoreVersion)) 
                ? "最新版本" 
                : _serverConfig.CoreVersion;
                
            CoreVersionText.Text = displayVer;
            MinecraftVersionText.Text = displayVer;
            
            _serverConfig.CoreType = "purpur";
            
            if (string.IsNullOrEmpty(_serverConfig.CoreVersion))
            {
                _serverConfig.CoreVersion = "latest";
                _serverConfig.MinecraftVersion = "latest";
            }
        }
        else
        {
            CoreTypeText.Text = _serverConfig.CoreType;
            CoreVersionText.Text = _serverConfig.CoreVersion;
            MinecraftVersionText.Text = _serverConfig.MinecraftVersion;
        }

        MinMemoryText.Text = $"{_serverConfig.MinMemoryMB} MB";
        MaxMemoryText.Text = $"{_serverConfig.MaxMemoryMB} MB";
        PortText.Text = _serverConfig.Port.ToString();
        JavaPathText.Text = string.IsNullOrEmpty(_serverConfig.JavaPath) ? "系统默认" : _serverConfig.JavaPath;

        if (_serverConfig.Mode == CreateMode.Advanced)
        {
            AdvancedConfigBorder.Visibility = Visibility.Visible;
            JvmArgsText.Text = string.IsNullOrEmpty(_serverConfig.JvmArgs) ? "无" : _serverConfig.JvmArgs;
            EulaText.Text = _serverConfig.AutoAcceptEula ? "是" : "否";
        }
    }

    private async void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCreating || _serverConfig == null) return;

        _isCreating = true;
        CreateButton.IsEnabled = false;
        ProgressBorder.Visibility = Visibility.Visible;
        ErrorInfoBar.IsOpen = false;

        try
        {
            await CreateServer();
        }
        catch (Exception ex)
        {
            HandleCreationError(ex);
        }
        finally
        {
            _isCreating = false;
            CreateButton.IsEnabled = true;
            ProgressBorder.Visibility = Visibility.Collapsed;
        }
    }

    private async Task CreateServer()
    {
        if (_serverConfig == null) return;

        ProgressStatusText.Text = "正在创建服务器目录...";
        await Task.Delay(300);

        Directory.CreateDirectory(_serverConfig.ServerPath);

        string jarPath;
        bool isForgeCore = IsForgeCoreType(_serverConfig.CoreType);

        if (!string.IsNullOrEmpty(_serverConfig.ImportSourcePath))
        {
            ProgressStatusText.Text = "正在导入本地核心...";
            var fileName = Path.GetFileName(_serverConfig.ImportSourcePath);
            jarPath = Path.Combine(_serverConfig.ServerPath, fileName);
            File.Copy(_serverConfig.ImportSourcePath, jarPath, true);

            if (isForgeCore && ForgeInstallerService.IsForgeInstaller(fileName))
            {
                _serverConfig.ForgeInstalled = false;
            }
        }
        else if (_serverConfig.UseLatestPurpur && _serverConfig.Mode == CreateMode.Beginner)
        {
            ProgressStatusText.Text = "正在下载服务端核心...";
            var version = _serverConfig.CoreVersion == "latest" ? "latest" : _serverConfig.CoreVersion;
            jarPath = await _downloadService.DownloadServerCoreAsync("purpur", version, _serverConfig.ServerPath);
        }
        else
        {
            if (isForgeCore)
            {
                ProgressStatusText.Text = $"正在下载 {_serverConfig.CoreType} 安装器...";
                jarPath = await _downloadService.DownloadForgeInstallerAsync(
                    _serverConfig.CoreType,
                    _serverConfig.CoreVersion,
                    _serverConfig.ServerPath
                );
                _serverConfig.ForgeInstalled = false;
            }
            else
            {
                ProgressStatusText.Text = "正在下载服务端核心...";
                jarPath = await _downloadService.DownloadServerCoreAsync(
                    _serverConfig.CoreType,
                    _serverConfig.CoreVersion,
                    _serverConfig.ServerPath
                );
            }
        }

        _serverConfig.JarFileName = Path.GetFileName(jarPath);

        if (isForgeCore && !_serverConfig.ForgeInstalled)
        {
            ProgressStatusText.Text = $"正在安装 {_serverConfig.CoreType}，这可能需要几分钟...";
            CreateProgressBar.IsIndeterminate = true;

            var javaPath = _serverConfig.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                javaPath = await _serverManager.DetectJavaAsync();
            }

            if (string.IsNullOrEmpty(javaPath))
            {
                throw new Exception("未找到Java环境，无法完成Forge安装。请先安装Java。");
            }

            var installResult = await _serverManager.DownloadAndInstallForgeAsync(
                _serverConfig.CoreType,
                _serverConfig.CoreVersion,
                _serverConfig.ServerPath,
                javaPath,
                default,
                (message, progress) =>
                {
                    ProgressStatusText.Text = message;
                    if (progress.HasValue)
                    {
                        CreateProgressBar.IsIndeterminate = false;
                        CreateProgressBar.Value = progress.Value;
                    }
                }
            );

            if (string.IsNullOrEmpty(installResult))
            {
                throw new Exception("Forge安装失败，请检查网络连接和Java环境。");
            }

            _serverConfig.JarFileName = installResult;
            _serverConfig.ForgeInstalled = true;
            CreateProgressBar.IsIndeterminate = false;
            CreateProgressBar.Value = 100;
        }

        ProgressStatusText.Text = "正在保存服务器配置...";
        await Task.Delay(300);

        await _serverManager.CreateServerAsync(_serverConfig);

        ProgressStatusText.Text = "创建完成！";
        await Task.Delay(500);

        Frame.Navigate(typeof(ServerDetailPage), _serverConfig);
    }

    private static bool IsForgeCoreType(string? coreType)
    {
        if (string.IsNullOrEmpty(coreType)) return false;
        return coreType.Equals("forge", StringComparison.OrdinalIgnoreCase) ||
               coreType.Equals("neoforge", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleCreationError(Exception ex)
    {
        ErrorInfoBar.IsOpen = true;
        ErrorInfoBar.Message = $"创建服务器失败: {ex.Message}";
        System.Diagnostics.Debug.WriteLine($"[CreateConfirmation] 创建失败: {ex}");
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorInfoBar.IsOpen = false;
        await CreateServer();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCreating)
        {
            Frame.GoBack();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isCreating)
        {
            Frame.Navigate(typeof(CreateModeSelectionPage));
        }
    }
}