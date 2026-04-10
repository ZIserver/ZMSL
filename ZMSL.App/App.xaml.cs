using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.UI.Dispatching;
using ZMSL.App.Services;
using ZMSL.App.ViewModels;
using ZMSL.App.Views;

namespace ZMSL.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;
    public static ZMSL.App.MainWindow? MainWindowInstance { get; private set; }
    public static UpdateService UpdateService => Services.GetRequiredService<UpdateService>();

    // 是否为内测版本
    public static readonly bool IsBeta = false;

    public App()
    {
        try
        {
            // 先配置服务，再初始化 XAML
            Services = ConfigureServices();

            this.InitializeComponent();
            
            // 强制加载 Markdown 程序集，防止 InitializeComponent 时找不到类型
            //_ = typeof(CommunityToolkit.WinUI.UI.Controls.MarkdownTextBlock);

            // 初始化子进程管理器（确保启动器关闭时子进程也会终止）
            _ = ChildProcessManager.Instance;

            // 初始化 Windows 通知
            AppNotificationManager.Default.Register();

            // 全局异常处理
            this.UnhandledException += App_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App 初始化失败: {ex}");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"UnhandledException: {e.Exception}");
        try
        {
            ChildProcessManager.Instance.TerminateAll();
        }
        catch { }
        // 如果 MainWindow 已经创建且可用，尝试显示错误对话框
        if (MainWindow?.Content?.XamlRoot != null)
        {
            try
            {
                var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                {
                    Title = "发生未知错误",
                    Content = e.Exception.ToString(),
                    CloseButtonText = "关闭",
                    XamlRoot = MainWindow.Content.XamlRoot
                };
                _ = dialog.ShowAsync();
            }
            catch { }
        }
        e.Handled = true;
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"UnobservedTaskException: {e.Exception}");
        try
        {
            ChildProcessManager.Instance.TerminateAll();
        }
        catch { }
        e.SetObserved();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // 检查是否需要在安装完成后重启应用
        CheckAndRestartAfterInstall();

        if (IsBeta)
        {
            var betaWindow = new Views.BetaVerifyWindow();
            betaWindow.Activate();
        }
        else
        {
            LaunchMainWindow();
        }
    }

    /// <summary>
    /// 检查安装完成后的重启标志，如果需要则重新启动应用并清理旧安装包
    /// </summary>
    private void CheckAndRestartAfterInstall()
    {
        try
        {
            var restartInfoFile = Path.Combine(Path.GetTempPath(), "zmsl_restart_after_install.txt");
            if (File.Exists(restartInfoFile))
            {
                // 读取并删除标志文件
                var restartPath = File.ReadAllText(restartInfoFile);
                File.Delete(restartInfoFile);

                // 验证路径是否有效
                if (!string.IsNullOrEmpty(restartPath) && File.Exists(restartPath))
                {
                    // 延迟启动，给安装程序一些时间完成安装
                    Task.Delay(2000).ContinueWith(_ =>
                    {
                        try
                        {
                            // 清理旧的更新安装包
                            CleanUpOldUpdateInstaller();

                            // 启动新安装的应用
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = restartPath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Restart] 重启应用失败：{ex.Message}");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Restart] 检查重启标志失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 清理旧的更新安装包
    /// </summary>
    private void CleanUpOldUpdateInstaller()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var updateFolder = Path.Combine(appData, "ZMSL", "Updates");
            var updateInstaller = Path.Combine(updateFolder, "update_installer.exe");

            if (File.Exists(updateInstaller))
            {
                File.Delete(updateInstaller);
                System.Diagnostics.Debug.WriteLine($"[CleanUp] 已清理旧安装包：{updateInstaller}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CleanUp] 清理安装包失败：{ex.Message}");
        }
    }

    public void LaunchMainWindow()
    {
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        MainWindowInstance = mainWindow;
        mainWindow.Activate();
        
        // 应用背景设置 - 使用 DispatcherQueue 而不是 Task.Run
        DispatcherQueue.GetForCurrentThread().TryEnqueue(async () =>
        {
            try
            {
                var db = Services.GetRequiredService<DatabaseService>();
                var settings = await db.GetSettingsAsync();
                if (settings.UseCustomBackground)
                {
                    MainWindowInstance?.UpdateCustomBackground(true, settings.BackgroundImagePath);
                }
                else
                {
                    MainWindowInstance?.UpdateSystemBackdrop(settings.EnableMicaEffect, settings.MicaIntensity);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"应用背景设置失败: {ex.Message}");
            }
        });
        
        // 启动自动备份服务（根据数据库设置）
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1000); // 延迟1秒
                var backupService = Services.GetRequiredService<BackupService>();
                await backupService.StartAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"启动备份服务失败: {ex.Message}");
            }
        });

        // 根据设置决定是否后台检查更新
        _ = CheckForUpdatesWhenEnabledAsync();

        // 尝试自动启动上次运行的服务器
        _ = AutoStartLastServerAsync();
    }

    private async Task AutoStartLastServerAsync()
    {
        try
        {
            // 延迟3秒等待应用完全加载
            await Task.Delay(3000);

            // 确保数据库已初始化
            var db = Services.GetRequiredService<DatabaseService>();
            await db.InitializeAsync();
            var settings = await db.GetSettingsAsync();

            // 立即应用窗口效果设置
            if (MainWindowInstance != null)
            {
                MainWindowInstance.UpdateSystemBackdrop(settings.EnableMicaEffect, settings.MicaIntensity);
            }

            if (settings.AutoStartLastServer)
            {
                var serverManager = Services.GetRequiredService<ServerManagerService>();
                var servers = await serverManager.GetServersAsync();
                // GetServersAsync 已经按 LastStartedAt 倒序排列
                var lastServer = servers.FirstOrDefault();
                
                if (lastServer != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoStart] 正在自动启动上次运行的服务器: {lastServer.Name}");
                    await serverManager.StartServerAsync(lastServer.Id);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AutoStart] 自动启动服务器失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 若用户开启自动检查更新，则延迟后执行检查并静默下载。
    /// </summary>
    private async Task CheckForUpdatesWhenEnabledAsync()
    {
        try
        {
            await Task.Delay(5000); // 延迟5秒后检查，让应用先加载

            var db = Services.GetRequiredService<DatabaseService>();
            await db.InitializeAsync();
            var settings = await db.GetSettingsAsync();
            if (!settings.AutoCheckUpdate)
                return;

            await CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"自动检查更新流程异常: {ex.Message}");
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var result = await UpdateService.CheckForUpdateAsync();
            if (result.Success && result.HasUpdate && !string.IsNullOrEmpty(result.DownloadUrl))
            {
                System.Diagnostics.Debug.WriteLine($"发现新版本: {result.LatestVersion}");
                
                try
                {
                    // 后台静默下载
                    var downloadResult = await UpdateService.DownloadUpdateAsync(result.DownloadUrl, result.FileHash);
                    if (downloadResult.Success)
                    {
                        // 发送通知告知用户更新已就绪
                        SendUpdateNotification(result.LatestVersion!);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"下载更新失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"检查更新失败: {ex.Message}");
        }
    }

    private void SendUpdateNotification(string newVersion)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText("新版本已就绪")
                .AddText($"智穗MC开服器 {newVersion} 已下载完成，重启应用后将自动更新")
                .BuildNotification();
            
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"发送通知失败: {ex.Message}");
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 注册数据库上下文
        services.AddSingleton<DatabaseService>();

        // 注册HTTP客户端
        services.AddHttpClient("ZmslApi", client =>
        {
            client.BaseAddress = new Uri("https://msl.v2.zhsdev.top/api/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(5); // 5秒超时
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // 使用系统默认的证书验证，而不是跳过验证
            ServerCertificateCustomValidationCallback = null
        });

        // 注册服务
        services.AddSingleton<ApiService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<ServerDownloadService>();
        services.AddSingleton<ForgeInstallerService>();
        services.AddSingleton<ServerManagerService>();
        services.AddSingleton<FrpService>();
        services.AddSingleton<FrpcDownloadService>();
        services.AddSingleton<MeFrpService>();
        services.AddSingleton<StarryFrpService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<JavaManagerService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<LinuxNodeService>();
        services.AddSingleton<LogAnalysisService>();

        // 注册ViewModels
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ServerCoreViewModel>();
        services.AddTransient<MyServerViewModel>();
        services.AddTransient<FrpViewModel>();
        services.AddTransient<MeFrpViewModel>();
        services.AddTransient<StarryFrpViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<LinuxNodeViewModel>();
        services.AddTransient<LinuxNodeDetailViewModel>();
        services.AddTransient<PlayerForumViewModel>();
        services.AddTransient<ForumPostDetailViewModel>();
        services.AddTransient<CreatePostViewModel>();
        services.AddTransient<UserProfileViewModel>();
        
        // 注册自动化管理服务
        services.AddSingleton<ScheduledRestartService>();
        services.AddSingleton<ResourceMonitoringService>();
        services.AddSingleton<AutoBackupService>();
        services.AddSingleton<PluginUpdateService>();
        services.AddSingleton<PlayerForumService>();

        return services.BuildServiceProvider();
    }
}
