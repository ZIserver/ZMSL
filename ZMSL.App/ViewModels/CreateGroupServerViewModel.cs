using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System.Text;
using Microsoft.EntityFrameworkCore;
using System.IO;

namespace ZMSL.App.ViewModels;

public partial class CreateGroupServerViewModel : ObservableObject
{
    private readonly ApiService _apiService;
    private readonly ServerManagerService _serverManager;
    private readonly DatabaseService _db;
    private readonly LinuxNodeService _nodeService;
    private readonly JavaManagerService _javaManager;
    private readonly ServerDownloadService _downloadService;

    [ObservableProperty]
        public partial string ServerName { get; set; } = "MyVelocityGroup";

        [ObservableProperty]
        public partial ObservableCollection<string> Versions { get; set; } = new();

        [ObservableProperty]
        public partial string? SelectedVersion { get; set; }

        [ObservableProperty]
        public partial int GroupPort { get; set; } = 25565;

        [ObservableProperty]
        public partial int MinMemory { get; set; } = 512;

        [ObservableProperty]
        public partial int MaxMemory { get; set; } = 1024;

        [ObservableProperty]
        public partial ObservableCollection<JavaInstallation> JavaList { get; set; } = new();

        [ObservableProperty]
        public partial JavaInstallation? SelectedJava { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial string StatusMessage { get; set; } = string.Empty;

    public CreateGroupServerViewModel(
        ApiService apiService, 
        ServerManagerService serverManager, 
        DatabaseService db,
        LinuxNodeService nodeService,
        JavaManagerService javaManager,
        ServerDownloadService downloadService)
    {
        _apiService = apiService;
        _serverManager = serverManager;
        _db = db;
        _nodeService = nodeService;
        _javaManager = javaManager;
        _downloadService = downloadService;
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsBusy = true;
        StatusMessage = "正在获取版本和 Java 列表...";
        try
        {
            // 应用镜像源设置
            var settings = await _db.GetSettingsAsync();
            _downloadService.SetMirrorSource(settings.DownloadMirrorSource ?? "MSL");
            
            // Load Versions
            var versions = await _downloadService.GetAvailableVersionsAsync("velocity");
            if (versions != null && versions.Count > 0)
            {
                App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
                {
                    Versions = new ObservableCollection<string>(versions);
                    SelectedVersion = Versions.FirstOrDefault();
                });
            }
            else
            {
                StatusMessage = "获取版本失败";
            }

            // Load Java from DB
            var dbJavas = await _db.ExecuteWithLockAsync(async context => 
                 await context.JavaInstallations
                     .Where(j => j.IsValid)
                     .OrderByDescending(j => j.Version)
                     .ToListAsync());
            
            var javaList = new List<JavaInstallation>();
            foreach (var dbJava in dbJavas)
            {
                javaList.Add(new JavaInstallation
                {
                    Path = dbJava.Path,
                    Version = dbJava.Version,
                    Source = dbJava.Source
                });
            }
                     
            if (javaList.Count == 0)
            {
                 // If DB is empty, try quick scan
                 javaList = await _javaManager.DetectInstalledJavaAsync();
                 
                 // Save to DB
                 if (javaList.Count > 0)
                 {
                    await _db.ExecuteWithLockAsync(async context =>
                    {
                        foreach (var java in javaList)
                        {
                            if (!await context.JavaInstallations.AnyAsync(j => j.Path == java.Path))
                            {
                                context.JavaInstallations.Add(java.ToModel());
                            }
                        }
                        await context.SaveChangesAsync();
                    });
                 }
            }

            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                JavaList = new ObservableCollection<JavaInstallation>(javaList);
                if (JavaList.Count > 0)
                {
                    SelectedJava = JavaList.FirstOrDefault();
                }
                else
                {
                     StatusMessage = "未检测到 Java 环境，请先安装 Java";
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() => IsBusy = false);
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
        {
            StatusMessage = "请输入服务器名称";
            return;
        }
        if (string.IsNullOrEmpty(SelectedVersion))
        {
            StatusMessage = "请选择版本";
            return;
        }
        if (SelectedJava == null)
        {
             StatusMessage = "请选择 Java 版本";
             return;
        }

        IsBusy = true;
        StatusMessage = "正在获取下载地址...";
        
        try
        {
            // 1. Download Velocity using ServerDownloadService
            var downloadData = await _downloadService.GetDownloadUrlAsync("velocity", SelectedVersion);
            if (downloadData == null || string.IsNullOrEmpty(downloadData.Url))
            {
                StatusMessage = "无法获取下载地址";
                return;
            }

            // Create Server Directory
            var settings = await _db.GetSettingsAsync();
            var basePath = !string.IsNullOrEmpty(settings.DefaultServerPath)
                ? settings.DefaultServerPath
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZMSL", "servers");
            var serverPath = Path.Combine(basePath, ServerName);
            
            if (Directory.Exists(serverPath))
            {
                StatusMessage = "服务器名称已存在";
                return;
            }
            Directory.CreateDirectory(serverPath);

            StatusMessage = "正在下载 Velocity...";
            var jarPath = await _downloadService.DownloadServerCoreAsync(
                "velocity",
                SelectedVersion,
                serverPath,
                "latest",
                CancellationToken.None
            );

            var jarName = Path.GetFileName(jarPath);

            // 2. Register Group Server
            var groupServer = new LocalServer
            {
                Name = ServerName,
                CoreType = "velocity",
                CoreVersion = SelectedVersion,
                MinecraftVersion = SelectedVersion,
                ServerPath = serverPath,
                JarFileName = jarName,
                Port = GroupPort,
                CreatedAt = DateTime.Now,
                Mode = CreateMode.Advanced,
                MinMemoryMB = MinMemory,
                MaxMemoryMB = MaxMemory,
                JavaPath = SelectedJava.Path
            };
            
            await _serverManager.CreateServerAsync(groupServer);

            // Generate Secret Early for Sub-servers
            var secretPath = Path.Combine(serverPath, "forwarding.secret");
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var secret = new string(Enumerable.Repeat(chars, 12).Select(s => s[random.Next(s.Length)]).ToArray());
            await File.WriteAllTextAsync(secretPath, secret);

            // 4. Generate velocity.toml
            StatusMessage = "正在生成配置...";
            await GenerateVelocityConfigAsync(serverPath, GroupPort, new Dictionary<string, string>());
            
            StatusMessage = "创建成功！";
            
            // Navigate back
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.MainWindowInstance?.ContentFramePublic.CanGoBack == true)
                    App.MainWindowInstance.ContentFramePublic.GoBack();
                else
                    App.MainWindowInstance?.NavigateToPage("GroupServer");
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"发生错误: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (App.MainWindowInstance?.ContentFramePublic.CanGoBack == true)
            App.MainWindowInstance.ContentFramePublic.GoBack();
        else
            App.MainWindowInstance?.NavigateToPage("GroupServer");
    }

    private string UpdatePropertiesContent(string content, int newPort)
    {
        var sb = new StringBuilder();
        var lines = content.Split('\n');
        bool portFound = false;
        bool onlineFound = false;
        
        foreach (var line in lines)
        {
            var trim = line.Trim();
            if (trim.StartsWith("server-port="))
            {
                sb.AppendLine($"server-port={newPort}");
                portFound = true;
            }
            else if (trim.StartsWith("online-mode="))
            {
                sb.AppendLine("online-mode=false");
                onlineFound = true;
            }
            else
            {
                sb.AppendLine(line);
            }
        }
        
        if (!portFound) sb.AppendLine($"server-port={newPort}");
        if (!onlineFound) sb.AppendLine("online-mode=false");
        
        return sb.ToString();
    }

    private async Task GenerateVelocityConfigAsync(string path, int port, Dictionary<string, string> servers)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"bind = \"0.0.0.0:{port}\"");
        sb.AppendLine("motd = \"&3A Velocity Server\"");
        sb.AppendLine("show-max-players = 500");
        sb.AppendLine("online-mode = true"); 
        sb.AppendLine("force-key-authentication = true");
        sb.AppendLine("prevent-client-proxy-connections = false");
        sb.AppendLine("player-info-forwarding-mode = \"modern\""); 
        sb.AppendLine("forwarding-secret-file = \"forwarding.secret\"");
        sb.AppendLine("announce-forge = false");
        sb.AppendLine("kick-existing-players = false");
        sb.AppendLine("ping-passthrough = \"DISABLED\"");
        sb.AppendLine("");
        sb.AppendLine("[servers]");
        
        foreach (var kvp in servers)
        {
            var safeName = System.Text.RegularExpressions.Regex.Replace(kvp.Key, "[^a-zA-Z0-9]", "");
            if (string.IsNullOrEmpty(safeName)) safeName = "server" + Math.Abs(kvp.Value.GetHashCode());
            
            sb.AppendLine($"{safeName} = \"{kvp.Value}\"");
        }
        
        if (servers.Any())
        {
            var firstSafeName = System.Text.RegularExpressions.Regex.Replace(servers.First().Key, "[^a-zA-Z0-9]", "");
            if (string.IsNullOrEmpty(firstSafeName)) firstSafeName = "server" + Math.Abs(servers.First().Value.GetHashCode());
            
            sb.AppendLine($"try = [\"{firstSafeName}\"]");
        }
        else
        {
            sb.AppendLine("try = []");
        }
        
        sb.AppendLine("");
        sb.AppendLine("[advanced]");
        sb.AppendLine("compression-threshold = 256");
        sb.AppendLine("compression-level = -1");
        sb.AppendLine("login-ratelimit = 3000");
        sb.AppendLine("connection-timeout = 5000");
        sb.AppendLine("read-timeout = 30000");
        sb.AppendLine("haproxy-protocol = false");
        sb.AppendLine("tcp-fast-open = false");
        sb.AppendLine("bungee-plugin-message-channel = true");
        sb.AppendLine("show-ping-requests = false");
        sb.AppendLine("failover-on-unexpected-server-disconnect = true");
        sb.AppendLine("announce-proxy-commands = true");
        sb.AppendLine("log-command-executions = false");
        
        var configPath = Path.Combine(path, "velocity.toml");
        await File.WriteAllTextAsync(configPath, sb.ToString());
        
        var secretPath = Path.Combine(path, "forwarding.secret");
        if (!File.Exists(secretPath))
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var secret = new string(Enumerable.Repeat(chars, 12).Select(s => s[random.Next(s.Length)]).ToArray());
            await File.WriteAllTextAsync(secretPath, secret);
        }
    }
}
