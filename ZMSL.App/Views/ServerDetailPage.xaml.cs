using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using ZMSL.App.Models;
using ZMSL.App.Services;
using MUIText = Microsoft.UI.Text;

namespace ZMSL.App.Views;

public sealed partial class ServerDetailPage : Page
{
    private readonly ServerManagerService _serverManager;
    private readonly DatabaseService _db;
    private readonly BackupService _backupService;
    private LocalServer? _server;
    
    private Microsoft.UI.Xaml.DispatcherTimer? _resourceTimer;
    private Microsoft.UI.Xaml.DispatcherTimer? _uptimeTimer;
    private ObservableCollection<string> _players = new();
    private List<CommandSuggestion> _allCommands = new();
    private List<CommandSuggestion> _filteredSuggestions = new();
    private DateTime? _serverStartTime;
    private Dictionary<string, string> _serverProperties = new();
    private bool _isForceStoppingServer = false; // 标记是否正在强制停止
    
    // 日志性能优化
    private const int MAX_LOG_LINES = 1000; // 最大保留日志行数
    private int _currentLogLines = 0;
    private readonly Queue<string> _logQueue = new(); // 日志队列
    private Microsoft.UI.Xaml.DispatcherTimer? _logFlushTimer; // 批量刷新定时器
    private readonly object _logQueueLock = new();

    public ServerDetailPage()
    {
        this.InitializeComponent();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _db = App.Services.GetRequiredService<DatabaseService>();
        _backupService = App.Services.GetRequiredService<BackupService>();

        PlayerListView.ItemsSource = _players;
        InitializeCommands();
    }
    
    private void InitializeCommands()
    {
        _allCommands = new List<CommandSuggestion>
        {
            // 权限管理
            new("/op {player}", "给予玩家管理员权限", true),
            new("/deop {player}", "移除玩家管理员权限", true),
            
            // 玩家管理
            new("/kick {player} [{reason}]", "踢出玩家", true),
            new("/ban {player} [{reason}]", "封禁玩家", true),
            new("/ban-ip {ip} [{reason}]", "封禁IP地址", true),
            new("/pardon {player}", "解除玩家封禁", true),
            new("/pardon-ip {ip}", "解除IP封禁", true),
            new("/banlist", "查看封禁列表", false),
            
            // 白名单管理
            new("/whitelist on", "开启白名单", false),
            new("/whitelist off", "关闭白名单", false),
            new("/whitelist add {player}", "添加玩家到白名单", true),
            new("/whitelist remove {player}", "从白名单移除玩家", true),
            new("/whitelist list", "查看白名单列表", false),
            new("/whitelist reload", "重载白名单", false),
            
            // 服务器管理
            new("/stop", "正确停止服务器（会自动保存）", false),
            new("/save-all", "立即保存所有世界数据", false),
            new("/save-on", "开启自动保存", false),
            new("/save-off", "关闭自动保存（慎用）", false),
            new("/reload", "重载服务器配置", false),
            new("/list", "查看在线玩家列表", false),
            new("/seed", "查看世界种子", false),
            
            // 世界设置
            new("/setworldspawn [{x} {y} {z}]", "设置世界出生点", false),
            new("/spawnpoint [{player}] [{x} {y} {z}]", "设置玩家重生点", true),
            new("/difficulty peaceful", "设置难度: 和平", false),
            new("/difficulty easy", "设置难度: 简单", false),
            new("/difficulty normal", "设置难度: 普通", false),
            new("/difficulty hard", "设置难度: 困难", false),
            
            // 时间和天气
            new("/time set day", "设置时间: 白天", false),
            new("/time set night", "设置时间: 夜晚", false),
            new("/time set noon", "设置时间: 正午", false),
            new("/time set midnight", "设置时间: 午夜", false),
            new("/time add {time}", "增加时间", false),
            new("/weather clear [{duration}]", "设置天气: 晴天", false),
            new("/weather rain [{duration}]", "设置天气: 雨天", false),
            new("/weather thunder [{duration}]", "设置天气: 雷雨", false),
            
            // 游戏模式
            new("/gamemode survival [{player}]", "更改游戏模式: 生存", true),
            new("/gamemode creative [{player}]", "更改游戏模式: 创造", true),
            new("/gamemode adventure [{player}]", "更改游戏模式: 冒险", true),
            new("/gamemode spectator [{player}]", "更改游戏模式: 旁观", true),
            new("/defaultgamemode {mode}", "设置默认游戏模式", false),
            
            // 游戏规则 - 常用
            new("/gamerule keepInventory true", "死亡不掉落物品", false),
            new("/gamerule showDeathMessages true", "显示死亡消息", false),
            new("/gamerule doImmediateRespawn true", "立即重生（跳过死亡界面）", false),
            new("/gamerule doMobSpawning false", "关闭生物自然生成", false),
            new("/gamerule mobGriefing false", "生物不破坏方块（苦力怕不炸方块）", false),
            new("/gamerule doInsomnia false", "关闭幻翼生成", false),
            new("/gamerule doDaylightCycle false", "停止时间流动", false),
            new("/gamerule doWeatherCycle false", "关闭天气变化", false),
            new("/gamerule doFireTick false", "火不蔓延不熄灭", false),
            new("/gamerule announceAdvancements true", "显示成就公告", false),
            new("/gamerule commandBlockOutput false", "关闭命令方块输出", false),
            new("/gamerule naturalRegeneration true", "自然生命恢复", false),
            new("/gamerule fallDamage true", "摔落伤害开关", false),
            
            // 传送
            new("/tp {player}", "传送到某玩家", true),
            new("/tp {player} {target}", "传送某玩家到目标玩家", true),
            new("/tp {player} {x} {y} {z}", "传送到坐标", true),
            new("/tp @a 0 100 0", "传送所有玩家到坐标", false),
            
            // 物品和效果
            new("/give {player} {item} [{amount}]", "给予物品", true),
            new("/clear [{player}] [{item}] [{amount}]", "清除物品", true),
            new("/enchant {player} {enchantment} [{level}]", "附魔手持物品", true),
            new("/effect give {player} {effect} [{duration}] [{level}]", "给予药水效果", true),
            new("/effect clear {player} [{effect}]", "清除药水效果", true),
            
            // 经验
            new("/xp add {player} {amount} points", "给予经验点", true),
            new("/xp add {player} {amount} levels", "给予经验等级", true),
            
            // 通讯
            new("/msg {player} {message}", "私聊玩家", true),
            new("/say {message}", "服务器公告（所有人可见）", false),
            new("/title {player} title {text}", "显示标题", true),
            new("/me {action}", "显示动作消息", false),
            
            // 实体管理
            new("/kill {target}", "杀死实体或玩家", true),
            new("/kill @e[type=item,distance=..10]", "清除附近10格内所有掉落物", false),
            new("/kill @e[type=!player]", "清除所有非玩家实体", false),
            
            // 选择器示例
            new("/effect give @a[gamemode=survival] night_vision 999999 1", "给予所有生存模式玩家夜视", false),
            new("/tp @p", "传送到最近的玩家", false),
            new("/give @a diamond 64", "给予所有玩家64个钻石", false)
        };
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LocalServer server)
        {
            _server = server;
            LoadServerInfo();
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;

        _serverManager.ServerOutput += OnOutputReceived;
        _serverManager.ServerStatusChanged += OnStatusChanged;
        _serverManager.ServerCrashed += OnServerCrashed;

        // 应用控制台字体大小设置
        ApplyConsoleFontSize();

        // 加载历史日志
        LoadHistoryLogs();

        // 检查server.properties是否存在
        CheckServerProperties();
        
        // 加载服务器概况
        LoadServerOverview();

        // 降低刷新频率,从2秒改为5秒,减少磁盘I/O
        _resourceTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _resourceTimer.Tick += ResourceTimer_Tick;
        _resourceTimer.Start();
        
        // 运行时长计时器（每秒更新）
        _uptimeTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _uptimeTimer.Tick += UptimeTimer_Tick;
        _uptimeTimer.Start();
        
        // 日志批量刷新定时器（每 200ms 刷新一次，降低频率）
        _logFlushTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _logFlushTimer.Tick += LogFlushTimer_Tick;
        _logFlushTimer.Start();

        UpdateButtonStates();
    }

    private void OnServerCrashed(object? sender, ServerCrashEventArgs e)
    {
        if (_server == null || e.ServerId != _server.Id) return;
        
        // 如果正在强制停止，忽略崩溃检测
        if (_isForceStoppingServer) return;

        DispatcherQueue.TryEnqueue(async () =>
        {
            // 构建插件/Mod信息显示
            var pluginInfo = "";
            if (e.PluginList.Count > 0)
                pluginInfo += $"\n插件数量: {e.PluginList.Count}";
            if (e.ModList.Count > 0)
                pluginInfo += $"\nMod数量: {e.ModList.Count}";

            var dialog = new ContentDialog
            {
                Title = "⚠️ 服务器崩溃检测",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"检测到服务器可能发生崩溃！",
                            TextWrapping = TextWrapping.Wrap,
                            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                        },
                        new TextBlock
                        {
                            Text = $"触发关键词: {e.DetectedKeyword}{pluginInfo}",
                            Opacity = 0.7,
                            FontSize = 12
                        },
                        new TextBlock
                        {
                            Text = "是否使用 AI 分析崩溃原因？AI 将分析启动命令、插件/Mod列表和完整日志来帮助诊断问题。",
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.8
                        }
                    }
                },
                PrimaryButtonText = "AI 分析",
                SecondaryButtonText = "查看日志",
                CloseButtonText = "忽略",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // 构建崩溃分析数据
                var crashData = new CrashAnalysisData
                {
                    FullLog = e.FullLog,
                    StartupCommand = e.StartupCommand,
                    PluginList = e.PluginList,
                    ModList = e.ModList
                };
                // 跳转到日志分析页面，传递完整崩溃数据
                Frame.Navigate(typeof(LogAnalysisPage), crashData);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                // 滚动到控制台底部查看日志
                ConsoleOutput.Focus(FocusState.Programmatic);
            }
        });
    }

    private async void ApplyConsoleFontSize()
    {
        try
        {
            var settings = await _db.GetSettingsAsync();
            var fontSize = settings.ConsoleFontSize > 0 ? settings.ConsoleFontSize : 12;
            ConsoleOutput.FontSize = fontSize;
            
            // 注册编码提供程序以支持 GB18030
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            System.Diagnostics.Debug.WriteLine($"[Console] 应用设置 - 字体大小: {fontSize}, 编码: {settings.ConsoleEncoding}");
        }
        catch
        {
            // 如果获取设置失败，使用默认值
            ConsoleOutput.FontSize = 12;
        }
    }

    private async void LoadHistoryLogs()
    {
        if (_server == null) return;
        
        // 获取设置中的编码
        var settings = await _db.GetSettingsAsync();
        Encoding logEncoding = GetEncodingFromSettings(settings.ConsoleEncoding);
        
        var logs = _serverManager.GetServerLogs(_server.Id);
        
        // 只加载最后的 MAX_LOG_LINES 行，避免历史日志过多导致卡顿
        var recentLogs = logs.TakeLast(MAX_LOG_LINES).ToList();
        
        // 临时取消只读以便清空和添加内容
        ConsoleOutput.IsReadOnly = false;
        
        try
        {
            // 清空控制台
            ConsoleOutput.Document.Selection.SetRange(0, int.MaxValue);
            ConsoleOutput.Document.Selection.Delete(MUIText.TextRangeUnit.Character, 0);
            
            _currentLogLines = 0;
            
            // 添加历史日志并着色
            foreach (var log in recentLogs)
            {
                AppendColoredLog(log, false); // 不检查行数限制
                ParsePlayerEvent(log);
            }
            
            _currentLogLines = recentLogs.Count;
        }
        finally
        {
            // 恢复只读状态
            ConsoleOutput.IsReadOnly = true;
        }
    }

    /// <summary>
    /// 根据设置获取对应的编码
    /// </summary>
    private Encoding GetEncodingFromSettings(string consoleEncoding)
    {
        // 注册编码提供程序以支持 GB18030
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        return consoleEncoding switch
        {
            "ANSI" => Encoding.Default,
            "GB18030" => GetGB18030Encoding(),
            "UTF-8" => Encoding.UTF8,
            _ => Encoding.UTF8
        };
    }

    /// <summary>
    /// 获取 GB18030 编码，如果不支持则降级到 GBK
    /// </summary>
    private Encoding GetGB18030Encoding()
    {
        try
        {
            return Encoding.GetEncoding("GB18030");
        }
        catch
        {
            return Encoding.GetEncoding("GBK");
        }
    }
    
    private void AppendColoredLog(string message, bool checkLimit = true)
    {
        // 直接使用新的预处理方式
        var processed = PreprocessLog(message);
        AppendProcessedLog(processed);
    }
    
    /// <summary>
    /// 清理旧日志，保留最后 70% 的内容（异步版本）
    /// </summary>
    private async void TrimOldLogsAsync()
    {
        try
        {
            // 获取文档总长度
            ConsoleOutput.Document.GetText(MUIText.TextGetOptions.None, out var fullText);
            
            if (string.IsNullOrEmpty(fullText)) return;
            
            // 在后台线程分割文本
            var newText = await Task.Run(() =>
            {
                var lines = fullText.Split('\n');
                var keepLines = (int)(MAX_LOG_LINES * 0.7);
                
                if (lines.Length <= keepLines) return null;
                
                return string.Join("\n", lines.TakeLast(keepLines));
            });
            
            if (newText == null) return;
            
            // 在 UI 线程更新文本
            ConsoleOutput.Document.Selection.SetRange(0, int.MaxValue);
            ConsoleOutput.Document.Selection.Delete(MUIText.TextRangeUnit.Character, 0);
            
            var range = ConsoleOutput.Document.GetRange(0, 0);
            range.Text = newText;
            
            _currentLogLines = (int)(MAX_LOG_LINES * 0.7);
            
            System.Diagnostics.Debug.WriteLine($"[Console] 清理旧日志，保留 {_currentLogLines} 行");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"清理旧日志失败: {ex.Message}");
        }
    }
    
    private void CheckServerProperties()
    {
        if (_server == null) return;
        var propertiesPath = Path.Combine(_server.ServerPath, "server.properties");
        GameSettingsButton.Visibility = File.Exists(propertiesPath) ? Visibility.Visible : Visibility.Collapsed;
        
        // 检查是否开启白名单
        CheckWhitelistEnabled();
    }

    private void CheckWhitelistEnabled()
    {
        if (_server == null) return;
        var propertiesPath = Path.Combine(_server.ServerPath, "server.properties");
        if (!File.Exists(propertiesPath))
        {
            WhitelistButton.Visibility = Visibility.Collapsed;
            return;
        }
        
        try
        {
            var lines = File.ReadAllLines(propertiesPath);
            var whitelistLine = lines.FirstOrDefault(l => l.StartsWith("white-list="));
            if (whitelistLine != null && whitelistLine.Contains("true"))
            {
                WhitelistButton.Visibility = Visibility.Visible;
            }
            else
            {
                WhitelistButton.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            WhitelistButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _serverManager.ServerOutput -= OnOutputReceived;
        _serverManager.ServerStatusChanged -= OnStatusChanged;
        _serverManager.ServerCrashed -= OnServerCrashed;
        _resourceTimer?.Stop();
        _uptimeTimer?.Stop();
        _logFlushTimer?.Stop();
    }

    private void LoadServerInfo()
    {
        if (_server == null) return;
        ServerNameText.Text = _server.Name;
        ServerTypeText.Text = $"{_server.CoreType} {_server.MinecraftVersion}";
        UpdateButtonStates();
        UpdateFeatureButtons();
    }
    
    private void LoadServerOverview()
    {
        if (_server == null) return;
        
        // 基本信息
        OverviewServerName.Text = _server.Name;
        OverviewGameVersion.Text = _server.MinecraftVersion;
        OverviewCoreType.Text = _server.CoreType;
        OverviewMemory.Text = $"{_server.MinMemoryMB}MB - {_server.MaxMemoryMB}MB";
        OverviewPort.Text = _server.Port.ToString();
        
        // 从 server.properties 读取配置
        LoadServerProperties();
        
        // 显示配置信息
        OverviewOnlineMode.Text = GetProperty("online-mode", "true") == "true" ? "开启" : "关闭";
        
        var gamemode = GetProperty("gamemode", "survival");
        OverviewGameMode.Text = gamemode switch
        {
            "survival" => "生存",
            "creative" => "创造",
            "adventure" => "冒险",
            "spectator" => "旁观",
            _ => gamemode
        };
        
        var difficulty = GetProperty("difficulty", "easy");
        OverviewDifficulty.Text = difficulty switch
        {
            "peaceful" => "和平",
            "easy" => "简单",
            "normal" => "普通",
            "hard" => "困难",
            _ => difficulty
        };
    }
    
    private void LoadServerProperties()
    {
        if (_server == null) return;
        
        _serverProperties.Clear();
        var propertiesPath = Path.Combine(_server.ServerPath, "server.properties");
        
        if (!File.Exists(propertiesPath)) return;
        
        try
        {
            var lines = File.ReadAllLines(propertiesPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    _serverProperties[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取 server.properties 失败: {ex.Message}");
        }
    }
    
    private string GetProperty(string key, string defaultValue = "")
    {
        return _serverProperties.TryGetValue(key, out var value) ? value : defaultValue;
    }
    
    private void UptimeTimer_Tick(object? sender, object e)
    {
        if (_server == null) return;
        
        var isRunning = _serverManager.IsServerRunning(_server.Id);
        
        if (isRunning)
        {
            if (_serverStartTime == null)
            {
                // 尝试从进程启动时间获取
                var process = _serverManager.GetServerProcess(_server.Id);
                if (process != null && !process.HasExited)
                {
                    try
                    {
                        _serverStartTime = process.StartTime;
                    }
                    catch
                    {
                        // 如果无法获取进程启动时间，使用当前时间
                        _serverStartTime = DateTime.Now;
                    }
                }
                else
                {
                    _serverStartTime = DateTime.Now;
                }
            }
            
            var uptime = DateTime.Now - _serverStartTime.Value;
            OverviewUptime.Text = FormatUptime(uptime);
        }
        else
        {
            _serverStartTime = null;
            OverviewUptime.Text = "未运行";
        }
    }
    
    private string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}天 {uptime.Hours}小时 {uptime.Minutes}分钟";
        }
        else if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}小时 {uptime.Minutes}分钟 {uptime.Seconds}秒";
        }
        else if (uptime.TotalMinutes >= 1)
        {
            return $"{(int)uptime.TotalMinutes}分钟 {uptime.Seconds}秒";
        }
        else
        {
            return $"{uptime.Seconds}秒";
        }
    }

    private void UpdateFeatureButtons()
    {
        if (_server == null) return;
        
        var coreType = _server.CoreType?.ToLower() ?? "";
        
        // Plugins support
        bool supportsPlugins = coreType.Contains("paper") || 
                               coreType.Contains("spigot") || 
                               coreType.Contains("bukkit") || 
                               coreType.Contains("purpur") || 
                               coreType.Contains("folia") ||
                               coreType.Contains("mohist") || 
                               coreType.Contains("arclight") || 
                               coreType.Contains("magma") || 
                               coreType.Contains("catserver");
                               
        if (PluginsButton != null)
            PluginsButton.Visibility = supportsPlugins ? Visibility.Visible : Visibility.Collapsed;
        
        // Mods support
        bool supportsMods = coreType.Contains("mohist") || 
                            coreType.Contains("forge") || 
                            coreType.Contains("fabric") ||
                            coreType.Contains("arclight") || 
                            coreType.Contains("magma") || 
                            coreType.Contains("catserver") ||
                            coreType.Contains("neoforge") ||
                            coreType.Contains("quilt");
                            
        if (ModsButton != null)
            ModsButton.Visibility = supportsMods ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Plugins_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        Frame.Navigate(typeof(PluginManagerPage), _server);
    }

    private void Mods_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        Frame.Navigate(typeof(ModsManagerPage), _server);
    }

    private void BackupManage_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        Frame.Navigate(typeof(BackupManagerPage), _server);
    }

    private void UpdateButtonStates()
    {
        var isRunning = _serverManager.IsServerRunning(_server?.Id ?? 0);
        
        // 更新菜单项状态
        StartMenuItem.IsEnabled = !isRunning;
        RestartMenuItem.IsEnabled = isRunning;
        StopMenuItem.IsEnabled = isRunning;
        ForceStopMenuItem.IsEnabled = isRunning;
    }

    private void ResourceTimer_Tick(object? sender, object e)
    {
        if (_server == null) return;
        
        var isRunning = _serverManager.IsServerRunning(_server.Id);
        if (!isRunning)
        {
            CpuProgressBar.Value = 0;
            CpuText.Text = "0%";
            MemoryProgressBar.Value = 0;
            MemoryText.Text = "0 MB";
            return;
        }

        try
        {
            var process = _serverManager.GetServerProcess(_server.Id);
            if (process != null && !process.HasExited)
            {
                process.Refresh();
                var memoryMb = process.WorkingSet64 / 1024 / 1024;
                var memoryPercent = (double)memoryMb / _server.MaxMemoryMB * 100;
                MemoryProgressBar.Value = Math.Min(memoryPercent, 100);
                MemoryText.Text = $"{memoryMb} MB";
            }
        }
        catch { }
    }

    private void OnOutputReceived(object? sender, ServerOutputEventArgs e)
    {
        if (_server == null || e.ServerId != _server.Id) return;
        
        // 将日志加入队列，而不是立即渲染
        lock (_logQueueLock)
        {
            _logQueue.Enqueue(e.Message);
        }
        
        // 解析玩家事件（这个比较轻量，可以立即处理）
        DispatcherQueue.TryEnqueue(() => ParsePlayerEvent(e.Message));
    }
    
    /// <summary>
    /// 批量刷新日志到 UI
    /// </summary>
    private async void LogFlushTimer_Tick(object? sender, object e)
    {
        List<string> logsToRender;
        
        lock (_logQueueLock)
        {
            if (_logQueue.Count == 0) return;
            
            // 一次最多处理 20 条日志，避免单次处理过多阻塞 UI
            var count = Math.Min(_logQueue.Count, 20);
            logsToRender = new List<string>(count);
            
            for (int i = 0; i < count; i++)
            {
                if (_logQueue.Count > 0)
                    logsToRender.Add(_logQueue.Dequeue());
            }
        }
        
        // 在后台线程预处理日志（解析 ANSI 等）
        var processedLogs = await Task.Run(() => 
        {
            return logsToRender.Select(log => PreprocessLog(log)).ToList();
        });
        
        // 在 UI 线程批量渲染
        foreach (var processedLog in processedLogs)
        {
            AppendProcessedLog(processedLog);
        }
    }
    
    /// <summary>
    /// 在后台线程预处理日志
    /// </summary>
    private ProcessedLogEntry PreprocessLog(string message)
    {
        var entry = new ProcessedLogEntry { OriginalText = message };
        
        // 解析 ANSI 颜色代码
        var pattern = @"\x1B\[([0-9;]+)m";
        var matches = Regex.Matches(message, pattern);
        
        if (matches.Count == 0)
        {
            entry.Segments.Add(new LogSegment { Text = message });
            return entry;
        }
        
        int lastIndex = 0;
        Color? currentColor = null;
        bool isBold = false;
        
        var ansiColors = GetAnsiColorMap();
        
        foreach (Match match in matches)
        {
            // 添加 ANSI 代码之前的文本
            if (match.Index > lastIndex)
            {
                var text = message.Substring(lastIndex, match.Index - lastIndex);
                if (!string.IsNullOrEmpty(text))
                {
                    entry.Segments.Add(new LogSegment 
                    { 
                        Text = text, 
                        Color = currentColor,
                        IsBold = isBold
                    });
                }
            }
            
            // 解析 ANSI 代码
            var codes = match.Groups[1].Value.Split(';');
            foreach (var codeStr in codes)
            {
                if (int.TryParse(codeStr.Trim(), out var code))
                {
                    if (code == 0)
                    {
                        currentColor = null;
                        isBold = false;
                    }
                    else if (code == 1)
                    {
                        isBold = true;
                    }
                    else if (code == 22)
                    {
                        isBold = false;
                    }
                    else if (ansiColors.ContainsKey(code))
                    {
                        currentColor = ansiColors[code];
                    }
                }
            }
            
            lastIndex = match.Index + match.Length;
        }
        
        // 添加最后剩余的文本
        if (lastIndex < message.Length)
        {
            var text = message.Substring(lastIndex);
            if (!string.IsNullOrEmpty(text))
            {
                entry.Segments.Add(new LogSegment 
                { 
                    Text = text, 
                    Color = currentColor,
                    IsBold = isBold
                });
            }
        }
        
        return entry;
    }
    
    /// <summary>
    /// 在 UI 线程渲染已处理的日志
    /// </summary>
    private void AppendProcessedLog(ProcessedLogEntry entry)
    {
        try
        {
            var wasReadOnly = ConsoleOutput.IsReadOnly;
            if (wasReadOnly)
                ConsoleOutput.IsReadOnly = false;
            
            try
            {
                // 检查是否需要清理旧日志
                if (_currentLogLines >= MAX_LOG_LINES)
                {
                    TrimOldLogsAsync();
                }
                
                // 渲染所有片段
                foreach (var segment in entry.Segments)
                {
                    var range = ConsoleOutput.Document.GetRange(int.MaxValue, int.MaxValue);
                    range.Text = segment.Text;
                    
                    if (segment.Color.HasValue)
                    {
                        var colorRange = ConsoleOutput.Document.GetRange(
                            range.StartPosition, 
                            range.StartPosition + segment.Text.Length);
                        colorRange.CharacterFormat.ForegroundColor = segment.Color.Value;
                        
                        if (segment.IsBold)
                            colorRange.CharacterFormat.Bold = MUIText.FormatEffect.On;
                    }
                }
                
                // 添加换行
                var newlineRange = ConsoleOutput.Document.GetRange(int.MaxValue, int.MaxValue);
                newlineRange.Text = "\n";
                
                _currentLogLines++;
                
                // 每 10 行才滚动一次，减少滚动操作
                if (_currentLogLines % 10 == 0)
                {
                    var scrollRange = ConsoleOutput.Document.GetRange(int.MaxValue, int.MaxValue);
                    scrollRange.ScrollIntoView(MUIText.PointOptions.Start);
                }
            }
            finally
            {
                if (wasReadOnly)
                    ConsoleOutput.IsReadOnly = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"渲染日志失败: {ex.Message}");
        }
    }
    
    private Dictionary<int, Color> GetAnsiColorMap()
    {
        return new Dictionary<int, Color>
        {
            [30] = Color.FromArgb(255, 0, 0, 0),
            [31] = Color.FromArgb(255, 170, 0, 0),
            [32] = Color.FromArgb(255, 0, 170, 0),
            [33] = Color.FromArgb(255, 170, 85, 0),
            [34] = Color.FromArgb(255, 0, 0, 170),
            [35] = Color.FromArgb(255, 170, 0, 170),
            [36] = Color.FromArgb(255, 0, 170, 170),
            [37] = Color.FromArgb(255, 170, 170, 170),
            [90] = Color.FromArgb(255, 85, 85, 85),
            [91] = Color.FromArgb(255, 255, 85, 85),
            [92] = Color.FromArgb(255, 85, 255, 85),
            [93] = Color.FromArgb(255, 255, 255, 85),
            [94] = Color.FromArgb(255, 85, 85, 255),
            [95] = Color.FromArgb(255, 255, 85, 255),
            [96] = Color.FromArgb(255, 85, 255, 255),
            [97] = Color.FromArgb(255, 255, 255, 255),
        };
    }

    private void ParsePlayerEvent(string message)
    {
        var joinMatch = Regex.Match(message, @"(\w+) joined the game");
        if (joinMatch.Success)
        {
            var name = joinMatch.Groups[1].Value;
            if (!_players.Contains(name)) _players.Add(name);
            UpdatePlayerUI();
        }

        var leaveMatch = Regex.Match(message, @"(\w+) left the game");
        if (leaveMatch.Success)
        {
            _players.Remove(leaveMatch.Groups[1].Value);
            UpdatePlayerUI();
        }
    }

    private void UpdatePlayerUI()
    {
        PlayerCountText.Text = $"{_players.Count}/20";
        NoPlayersText.Visibility = _players.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStatusChanged(object? sender, ServerStatusEventArgs e)
    {
        if (_server == null || e.ServerId != _server.Id) return;
        DispatcherQueue.TryEnqueue(UpdateButtonStates);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        // 固定返回到服务器列表页面，避免返回到创建向导等中间页面
        Frame.Navigate(typeof(MyServerPage));
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;

        var result = await _serverManager.StartServerAsync(_server);

        if (result == ServerStartResult.JavaNotFound)
        {
            var dialog = new ContentDialog
            {
                Title = "找不到 Java",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "未找到可用的 Java 运行环境，无法启动服务器。", TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = "请前往 Java 管理页面下载或配置 Java。", Opacity = 0.7, FontSize = 12 }
                    }
                },
                PrimaryButtonText = "前往 Java 管理",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult == ContentDialogResult.Primary)
            {
                Frame.Navigate(typeof(JavaManagePage));
            }
            return;
        }

        if (result == ServerStartResult.NeedEulaAccept)
        {
            // 弹出 EULA 确认对话框
            var dialog = new ContentDialog
            {
                Title = "Minecraft EULA 协议",
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "要运行 Minecraft 服务器，您需要同意 Mojang 的最终用户许可协议 (EULA)。", TextWrapping = TextWrapping.Wrap },
                        new HyperlinkButton { Content = "查看 EULA 协议", NavigateUri = new Uri("https://aka.ms/MinecraftEULA") },
                        new TextBlock { Text = "点击\"同意\"表示您已阅读并同意该协议。", Opacity = 0.7, FontSize = 12 }
                    }
                },
                PrimaryButtonText = "同意并启动",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var dialogResult = await dialog.ShowAsync();
            if (dialogResult == ContentDialogResult.Primary)
            {
                // 用户同意，写入 EULA 并启动
                await _serverManager.AcceptEulaAsync(_server.ServerPath);
                result = await _serverManager.StartServerAsync(_server);
            }
        }
        
        if (result == ServerStartResult.Success || result == ServerStartResult.AlreadyRunning)
        {
            UpdateButtonStates();
            if (App.MainWindow is MainWindow mw) mw.UpdateServerStatus(true, _server.Name);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        await _serverManager.StopServerAsync(_server.Id);
        UpdateButtonStates();
        if (App.MainWindow is MainWindow mw) mw.UpdateServerStatus(false);
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        
        var dialog = new ContentDialog
        {
            Title = "确认重启",
            Content = "确定要重启服务器吗？这将断开所有玩家的连接。",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 先停止服务器
            await _serverManager.StopServerAsync(_server.Id);
            
            // 等待服务器完全停止
            await Task.Delay(2000);
            
            // 重新启动
            var startResult = await _serverManager.StartServerAsync(_server);
            if (startResult == ServerStartResult.Success || startResult == ServerStartResult.AlreadyRunning)
            {
                UpdateButtonStates();
                if (App.MainWindow is MainWindow mw) mw.UpdateServerStatus(true, _server.Name);
            }
        }
    }

    private async void ForceStop_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        
        var dialog = new ContentDialog
        {
            Title = "确认强制停止",
            Content = "强制停止可能导致数据丢失或损坏。建议使用普通停止。\n\n确定要强制停止服务器吗？",
            PrimaryButtonText = "强制停止",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // 设置标志，禁用崩溃检测
            _isForceStoppingServer = true;
            
            var process = _serverManager.GetServerProcess(_server.Id);
            if (process != null && !process.HasExited)
            {
                try
                {
                    process.Kill();
                    UpdateButtonStates();
                    if (App.MainWindow is MainWindow mw) mw.UpdateServerStatus(false);
                    
                    // 等待一段时间后重置标志
                    await Task.Delay(2000);
                    _isForceStoppingServer = false;
                    
                    // 显示提示
                    var infoDialog = new ContentDialog
                    {
                        Title = "服务器已强制停止",
                        Content = "服务器进程已被强制终止。",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await infoDialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    _isForceStoppingServer = false;
                    
                    var errorDialog = new ContentDialog
                    {
                        Title = "强制停止失败",
                        Content = $"无法强制停止服务器：{ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
            else
            {
                _isForceStoppingServer = false;
            }
        }
    }

    private async void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null || string.IsNullOrWhiteSpace(CommandInput.Text)) return;
        await _serverManager.SendCommandAsync(_server.Id, CommandInput.Text);
        CommandInput.Text = "";
    }

    private async void CommandInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await _serverManager.SendCommandAsync(_server?.Id ?? 0, CommandInput.Text);
            CommandInput.Text = "";
            CommandSuggestionPopup.IsOpen = false;
        }
        else if (e.Key == Windows.System.VirtualKey.Tab && _filteredSuggestions.Count > 0)
        {
            e.Handled = true;
            var suggestion = _filteredSuggestions[0];
            var currentInput = CommandInput.Text;

            if (suggestion.Command.StartsWith("/"))
            {
                var cmd = suggestion.Command.Split(' ')[0];
                
                // 如果命令需要玩家参数且有在线玩家，填充第一个玩家
                if (suggestion.RequiresPlayer && _players.Count > 0)
                {
                    var playerParam = suggestion.Command.Contains("{player}") ? _players[0] : "";
                    CommandInput.Text = $"{cmd} {playerParam}";
                }
                else
                {
                    CommandInput.Text = cmd + " ";
                }
            }
            else
            {
                // 参数补全：保留前面的部分，只替换或追加当前参数
                if (currentInput.EndsWith(" "))
                {
                    // 这是一个新参数
                    CommandInput.Text = currentInput + suggestion.Command + " ";
                }
                else
                {
                    // 替换当前正在输入的参数
                    var parts = currentInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        parts[^1] = suggestion.Command;
                        CommandInput.Text = string.Join(" ", parts) + " ";
                    }
                    else
                    {
                        CommandInput.Text = suggestion.Command + " ";
                    }
                }
            }
            
            CommandInput.SelectionStart = CommandInput.Text.Length;
            CommandSuggestionPopup.IsOpen = false;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CommandSuggestionPopup.IsOpen = false;
        }
    }
    
    private void CommandInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var input = CommandInput.Text.Trim();
        
        System.Diagnostics.Debug.WriteLine($"[Command] Input: '{input}'");
        
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("/"))
        {
            CommandSuggestionPopup.IsOpen = false;
            return;
        }
        
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var baseCommand = parts[0];
        
        // 检查是否在输入参数
        if (parts.Length > 1)
        {
            // 已经输入了命令，现在可能在输入参数
            var matchedCommand = _allCommands.FirstOrDefault(c => 
                c.Command.Split(' ')[0].Equals(baseCommand, StringComparison.OrdinalIgnoreCase));
            
            if (matchedCommand != null)
            {
                // 提取命令模板中的参数
                var templateParts = matchedCommand.Command.Split(' ');
                var currentParamIndex = parts.Length - 1; // 当前正在输入的参数位置
                
                if (currentParamIndex < templateParts.Length)
                {
                    var currentParam = templateParts[currentParamIndex];
                    
                    // 如果当前参数是 {player}，显示玩家列表
                    if (currentParam.Contains("{player}") && _players.Count > 0)
                    {
                        var lastInput = parts[^1];
                        _filteredSuggestions = _players
                            .Where(p => p.StartsWith(lastInput, StringComparison.OrdinalIgnoreCase))
                            .Select(p => new CommandSuggestion(p, "在线玩家", false))
                            .Take(10)
                            .ToList();
                        
                        if (_filteredSuggestions.Count > 0)
                        {
                            ShowSuggestionPopup();
                            return;
                        }
                    }
                    // 如果当前参数是 {mode}，显示游戏模式
                    else if (currentParam.Contains("{mode}"))
                    {
                        var modes = new[] 
                        { 
                            new CommandSuggestion("survival", "生存模式", false),
                            new CommandSuggestion("creative", "创造模式", false),
                            new CommandSuggestion("adventure", "冒险模式", false),
                            new CommandSuggestion("spectator", "旁观模式", false)
                        };
                        
                        var lastInput = parts[^1];
                        _filteredSuggestions = modes
                            .Where(m => m.Command.StartsWith(lastInput, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        
                        if (_filteredSuggestions.Count > 0)
                        {
                            ShowSuggestionPopup();
                            return;
                        }
                    }
                    // 如果当前参数是 {target}，显示玩家或选择器
                    else if (currentParam.Contains("{target}") && _players.Count > 0)
                    {
                        var lastInput = parts[^1];
                        _filteredSuggestions = _players
                            .Where(p => p.StartsWith(lastInput, StringComparison.OrdinalIgnoreCase))
                            .Select(p => new CommandSuggestion(p, "在线玩家", false))
                            .Concat(new[] 
                            {
                                new CommandSuggestion("@a", "所有玩家", false),
                                new CommandSuggestion("@p", "最近的玩家", false),
                                new CommandSuggestion("@r", "随机玩家", false),
                                new CommandSuggestion("@e", "所有实体", false)
                            }.Where(s => s.Command.StartsWith(lastInput, StringComparison.OrdinalIgnoreCase)))
                            .Take(10)
                            .ToList();
                        
                        if (_filteredSuggestions.Count > 0)
                        {
                            ShowSuggestionPopup();
                            return;
                        }
                    }
                }
            }
            
            CommandSuggestionPopup.IsOpen = false;
            return;
        }
        
        // 过滤命令（只输入了命令部分）
        _filteredSuggestions = _allCommands
            .Where(c => c.Command.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
        
        System.Diagnostics.Debug.WriteLine($"[Command] Filtered: {_filteredSuggestions.Count} results");
        
        if (_filteredSuggestions.Count > 0)
        {
            ShowSuggestionPopup();
        }
        else
        {
            CommandSuggestionPopup.IsOpen = false;
        }
    }
    
    private void ShowSuggestionPopup()
    {
        SuggestionListView.ItemsSource = _filteredSuggestions;
        
        try
        {
            // 获取输入框相对于窗口的位置
            var transform = CommandInput.TransformToVisual(null);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            
            // 计算合适的显示位置
            var windowHeight = 800.0;
            try
            {
                if (App.MainWindow != null)
                {
                    windowHeight = App.MainWindow.Bounds.Height;
                }
            }
            catch { }

            // 测量建议列表的高度
            double popupHeight = 300; // 默认最大高度
            if (CommandSuggestionPopup.Child is FrameworkElement popupChild)
            {
                popupChild.Measure(new Windows.Foundation.Size(500, 300));
                popupHeight = popupChild.DesiredSize.Height;
                // 如果测量结果太小（可能因为还没渲染），根据条目数估算
                if (popupHeight < 20 && _filteredSuggestions.Count > 0)
                {
                    // 估算高度：每项约56px + 上下Padding
                    popupHeight = Math.Min(_filteredSuggestions.Count * 56 + 10, 300);
                }
            }
            
            var spaceBelow = windowHeight - point.Y - CommandInput.ActualHeight;
            var spaceAbove = point.Y;
            
            System.Diagnostics.Debug.WriteLine($"[Command] Window H={windowHeight}, Input Y={point.Y}, Space Below={spaceBelow}, Space Above={spaceAbove}, Popup H={popupHeight}");
            
            // 策略：优先显示在空间更大的一侧。
            // 对于底部输入框，通常 Space Above >> Space Below，所以会显示在上方。
            // 只有当输入框在顶部（Space Below 更大）且下方空间足够时，才显示在下方。
            
            bool showAbove = true; // 默认为上方
            
            if (spaceBelow > spaceAbove && spaceBelow > popupHeight)
            {
                showAbove = false;
            }
            
            if (showAbove)
            {
                // 显示在输入框上方，紧贴输入框
                // 如果上方空间不足以放下完整的 popupHeight，则调整高度或位置（这里简单处理为紧贴）
                CommandSuggestionPopup.HorizontalOffset = point.X;
                CommandSuggestionPopup.VerticalOffset = point.Y - popupHeight - 4;
            }
            else
            {
                // 显示在输入框下方
                CommandSuggestionPopup.HorizontalOffset = point.X;
                CommandSuggestionPopup.VerticalOffset = point.Y + CommandInput.ActualHeight + 4;
            }
            
            CommandSuggestionPopup.IsOpen = true;
            System.Diagnostics.Debug.WriteLine($"[Command] Popup IsOpen: {CommandSuggestionPopup.IsOpen}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Command] Error: {ex.Message}");
        }
    }
    
    private void CommandInput_LostFocus(object sender, RoutedEventArgs e)
    {
        // 延迟关闭，给点击提示项留出时间
        Task.Delay(200).ContinueWith(_ =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                CommandSuggestionPopup.IsOpen = false;
            });
        });
    }
    
    private void SuggestionItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CommandSuggestion suggestion)
        {
            var currentInput = CommandInput.Text.Trim();
            var parts = currentInput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // 如果是命令建议（以 / 开头）
            if (suggestion.Command.StartsWith("/"))
            {
                var cmd = suggestion.Command.Split(' ')[0];
                
                // 如果命令需要玩家参数且有在线玩家，填充第一个玩家
                if (suggestion.RequiresPlayer && _players.Count > 0)
                {
                    CommandInput.Text = $"{cmd} {_players[0]}";
                }
                else
                {
                    CommandInput.Text = cmd + " ";
                }
            }
            // 如果是参数建议（不以 / 开头）
            else
            {
                // 替换最后一个参数
                if (parts.Length > 1)
                {
                    parts[^1] = suggestion.Command;
                    CommandInput.Text = string.Join(" ", parts) + " ";
                }
                else
                {
                    CommandInput.Text = currentInput + " " + suggestion.Command + " ";
                }
            }
            
            CommandSuggestionPopup.IsOpen = false;
            CommandInput.SelectionStart = CommandInput.Text.Length;
            CommandInput.Focus(FocusState.Programmatic);
        }
    }

    private async void QuickCmd_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is Button btn && btn.Tag is string cmd)
            await _serverManager.SendCommandAsync(_server.Id, cmd);
    }

    private async void KickPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            await _serverManager.SendCommandAsync(_server.Id, $"kick {name}");
            _players.Remove(name);
            UpdatePlayerUI();
        }
    }

    private async void GiveOp_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            await _serverManager.SendCommandAsync(_server.Id, $"op {name}");
        }
    }

    private async void RemoveOp_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            await _serverManager.SendCommandAsync(_server.Id, $"deop {name}");
        }
    }

    private async void BanPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            await _serverManager.SendCommandAsync(_server.Id, $"ban {name}");
        }
    }

    private async void PardonPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is MenuFlyoutItem item && item.Tag is string name)
        {
            await _serverManager.SendCommandAsync(_server.Id, $"pardon {name}");
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        var dialog = new ServerSettingsDialog(_server) { XamlRoot = this.XamlRoot };
        await dialog.ShowAsync();
        if (dialog.ServerDeleted)
        {
            if (Frame.CanGoBack) Frame.GoBack();
            else Frame.Navigate(typeof(MyServerPage));
        }
        else LoadServerInfo();
    }

    private void FileManage_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        Frame.Navigate(typeof(FileManagerPage), _server.ServerPath);
    }

    private async void GameSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        var dialog = new ServerPropertiesDialog(_server.ServerPath) { XamlRoot = this.XamlRoot };
        var result = await dialog.ShowAsync();
        // 如果修改了配置，重新检查白名单状态
        if (result == ContentDialogResult.Primary)
        {
            CheckWhitelistEnabled();
        }
    }

    private async void Whitelist_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        
        var whitelistPath = Path.Combine(_server.ServerPath, "whitelist.json");
        var whitelistData = new List<WhitelistEntry>();
        
        // 读取白名单文件
        if (File.Exists(whitelistPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(whitelistPath);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<WhitelistEntry>>(json);
                if (list != null)
                {
                    whitelistData = list;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Whitelist] 读取文件失败：{ex.Message}");
            }
        }
        
        // 保存白名单到文件
        async Task SaveWhitelistAsync()
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(whitelistData, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(whitelistPath, json);
                
                // 如果服务器正在运行，执行 whitelist reload 命令
                if (_server != null && _serverManager.IsServerRunning(_server.Id))
                {
                    await _serverManager.SendCommandAsync(_server.Id, "whitelist reload");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Whitelist] 保存文件失败：{ex.Message}");
            }
        }
        
        // 创建列表视图
        var stackPanel = new StackPanel { Spacing = 8 };
        
        void RebuildList()
        {
            stackPanel.Children.Clear();
            
            if (whitelistData.Count == 0)
            {
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = "白名单为空", 
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20),
                    Opacity = 0.6
                });
            }
            else
            {
                foreach (var entry in whitelistData.ToList())
                {
                    var border = new Border
                    {
                        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.Gray) { Opacity = 0.1 },
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12, 8, 12, 8)
                    };
                    
                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    
                    var nameText = new TextBlock
                    {
                        Text = entry.Name,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 14
                    };
                    Grid.SetColumn(nameText, 0);
                    
                    var deleteBtn = new Button
                    {
                        Content = "删除",
                        Padding = new Thickness(12, 4, 12, 4),
                        Tag = entry
                    };
                    Grid.SetColumn(deleteBtn, 1);
                    
                    deleteBtn.Click += async (s, args) =>
                    {
                        if (s is Button btn && btn.Tag is WhitelistEntry entryToRemove)
                        {
                            try
                            {
                                // 从列表中移除
                                whitelistData.RemoveAll(e => e.Name == entryToRemove.Name);
                                
                                // 保存到文件
                                await SaveWhitelistAsync();
                                
                                // 重建UI列表
                                RebuildList();
                                
                                System.Diagnostics.Debug.WriteLine($"[Whitelist] 成功删除玩家：{entryToRemove.Name}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Whitelist] 删除玩家失败：{ex.Message}");
                            }
                        }
                    };
                    
                    grid.Children.Add(nameText);
                    grid.Children.Add(deleteBtn);
                    border.Child = grid;
                    stackPanel.Children.Add(border);
                }
            }
        }
        
        RebuildList();
        
        var scrollViewer = new ScrollViewer
        {
            Content = stackPanel,
            MaxHeight = 400,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        
        var addBox = new TextBox { PlaceholderText = "输入玩家名称", Margin = new Thickness(0, 16, 0, 0) };
        var addButton = new Button { Content = "添加", Margin = new Thickness(8, 0, 0, 0) };
        
        addButton.Click += async (s, args) =>
        {
            var playerName = addBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(playerName)) return;
            
            // 检查是否已存在
            if (whitelistData.Any(e => e.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
            {
                addBox.Text = "";
                return;
            }
            
            try
            {
                // 使用命令添加玩家（让服务器生成UUID）
                if (_server != null)
                {
                    await _serverManager.SendCommandAsync(_server.Id, $"whitelist add {playerName}");
                }
                
                // 等待一小段时间让服务器写入文件
                await Task.Delay(500);
                
                // 重新读取whitelist.json
                if (File.Exists(whitelistPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(whitelistPath);
                        var list = System.Text.Json.JsonSerializer.Deserialize<List<WhitelistEntry>>(json);
                        if (list != null)
                        {
                            whitelistData.Clear();
                            whitelistData.AddRange(list);
                        }
                    }
                    catch { }
                }
                
                // 重建UI列表
                RebuildList();
                
                addBox.Text = "";
                System.Diagnostics.Debug.WriteLine($"[Whitelist] 成功添加玩家：{playerName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Whitelist] 添加玩家失败：{ex.Message}");
            }
        };
        
        var addPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { addBox, addButton }
        };
        
        var contentPanel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock 
                { 
                    Text = "点击右侧删除按钮可以移除玩家", 
                    FontSize = 12,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)
                },
                scrollViewer,
                addPanel
            }
        };
        
        var dialog = new ContentDialog
        {
            Title = "📋 白名单管理",
            Content = contentPanel,
            CloseButtonText = "关闭",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.Close
        };
        
        await dialog.ShowAsync();
    }

    private class WhitelistEntry
    {
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private void ConsoleOutput_SelectionChanged(object sender, RoutedEventArgs e)
    {
        // 当用户选择文本时，可以选择复制等操作
        // 这里可以添加复制到剪贴板等功能
    }

    private void CopyConsole_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOutput.Document.Selection.GetText(MUIText.TextGetOptions.None, out var selectedText);
        if (!string.IsNullOrEmpty(selectedText))
        {
            var package = new DataPackage();
            package.SetText(selectedText);
            Clipboard.SetContent(package);
        }
    }

    private void SelectAllConsole_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOutput.Document.Selection.SetRange(0, int.MaxValue);
    }

    private void AnalyzeSelected_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOutput.Document.Selection.GetText(MUIText.TextGetOptions.None, out var selectedText);
        
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            // 如果没有选中内容，使用全部日志
            ConsoleOutput.Document.GetText(MUIText.TextGetOptions.None, out selectedText);
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        // 导航到日志分析页面
        Frame.Navigate(typeof(LogAnalysisPage), selectedText);
    }
    
    // 日志预处理数据结构
    private class ProcessedLogEntry
    {
        public string OriginalText { get; set; } = string.Empty;
        public List<LogSegment> Segments { get; set; } = new();
    }
    
    private class LogSegment
    {
        public string Text { get; set; } = string.Empty;
        public Color? Color { get; set; }
        public bool IsBold { get; set; }
    }
    
    private class CommandSuggestion
    {
        public string Command { get; set; }
        public string Description { get; set; }
        public bool RequiresPlayer { get; set; }
        
        public CommandSuggestion(string command, string description, bool requiresPlayer)
        {
            Command = command;
            Description = description;
            RequiresPlayer = requiresPlayer;
        }
    }
}
