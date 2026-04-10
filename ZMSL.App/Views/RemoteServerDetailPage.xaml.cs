using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Windows.ApplicationModel.DataTransfer;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class RemoteServerDetailPage : Page
{
    private readonly LinuxNodeService _nodeService;
    private LinuxNode? _currentNode;
    private RemoteServer? _currentServer;
    private DispatcherTimer? _refreshTimer;
    private ObservableCollection<string> _players = new();
    private ObservableCollection<BackupInfo> _backups = new();
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _wsCts;

    public RemoteServerDetailPage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
        PlayerListView.ItemsSource = _players;
        BackupListView.ItemsSource = _backups;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.Parameter is (LinuxNode node, RemoteServer server))
        {
            _currentNode = node;
            _currentServer = server;
            await LoadServerInfo();
            StartRefreshTimer();
            await ConnectWebSocket();
            await LoadBackups();
        }
        else
        {
            Frame.GoBack();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopRefreshTimer();
        DisconnectWebSocket();
    }

    private void StartRefreshTimer()
    {
        _refreshTimer = new DispatcherTimer();
        // 降低刷新频率,从3秒改为10秒,减少网络和磁盘I/O
        _refreshTimer.Interval = TimeSpan.FromSeconds(10);
        _refreshTimer.Tick += async (s, e) => await RefreshStatus();
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private async Task LoadServerInfo()
    {
        if (_currentServer == null) return;

        ServerNameText.Text = _currentServer.Name;
        ServerTypeText.Text = $"{_currentServer.CoreType} {_currentServer.MinecraftVersion}";
        
        await RefreshStatus();
    }

    private async Task RefreshStatus()
    {
        if (_currentNode == null || _currentServer == null) return;

        try
        {
            var status = await _nodeService.GetServerStatusAsync(_currentNode, _currentServer.RemoteServerId);
            if (status != null)
            {
                var wasRunning = StopButton?.IsEnabled ?? false; // 之前是否在运行
                var isRunning = status.Running;
                
                // 更新按钮状态
                if (StartButton != null) StartButton.IsEnabled = !status.Running;
                if (StopButton != null) StopButton.IsEnabled = status.Running;

                // 更新资源信息
                if (CpuProgressBar != null) CpuProgressBar.Value = status.CpuPercent;
                if (CpuText != null) CpuText.Text = $"{status.CpuPercent:F1}%";
                
                long memoryMB = status.MemoryUsed / 1024 / 1024;
                if (MemoryProgressBar != null) MemoryProgressBar.Value = status.MemoryPercent;
                if (MemoryText != null) MemoryText.Text = $"{memoryMB} MB";

                // 解析玩家列表（从日志中）
                // TODO: 实现玩家列表解析
                UpdatePlayersList();
                
                // 如果服务器从停止变为运行，重新连接 WebSocket
                if (!wasRunning && isRunning && (_webSocket == null || _webSocket.State != WebSocketState.Open))
                {
                    ConsoleOutput.Text += "[\u7cfb\u7edf] \u68c0\u6d4b\u5230\u670d\u52a1\u5668\u542f\u52a8\uff0c\u91cd\u65b0\u8fde\u63a5\u63a7\u5236\u53f0...\n";
                    await ConnectWebSocket();
                }
                // 如果服务器停止，断开 WebSocket
                else if (wasRunning && !isRunning && _webSocket != null)
                {
                    ConsoleOutput.Text += "[\u7cfb\u7edf] \u670d\u52a1\u5668\u5df2\u505c\u6b62\n";
                    DisconnectWebSocket();
                }
            }
        }
        catch
        {
            // 忽略刷新错误
        }
    }

    private void UpdatePlayersList()
    {
        // TODO: 从服务器日志解析在线玩家
        NoPlayersText.Visibility = _players.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayerListView.Visibility = _players.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PlayerCountText.Text = $"{_players.Count}/20";
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        var (success, message) = await _nodeService.StartServerAsync(_currentNode, _currentServer.RemoteServerId);
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (success)
        {
            ConsoleOutput.Text += $"[系统] {message}\n";
            await Task.Delay(500);
            await RefreshStatus();
        }
        else
        {
            await ShowErrorDialog(message);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        var (success, message) = await _nodeService.StopServerAsync(_currentNode, _currentServer.RemoteServerId);
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (success)
        {
            ConsoleOutput.Text += $"[系统] {message}\n";
            await Task.Delay(500);
            await RefreshStatus();
        }
        else
        {
            await ShowErrorDialog(message);
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;

        var dialog = new ContentDialog
        {
            Title = "确认重启",
            Content = "确定要重启服务器吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        var (success, message) = await _nodeService.RestartServerAsync(_currentNode, _currentServer.RemoteServerId);
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (success)
        {
            ConsoleOutput.Text += $"[系统] {message}\n";
        }
        else
        {
            await ShowErrorDialog(message);
        }
    }

    private async void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        await SendCommand();
    }

    private async void CommandInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await SendCommand();
            e.Handled = true;
        }
    }

    private async Task SendCommand()
    {
        if (_currentNode == null || _currentServer == null) return;
        if (string.IsNullOrWhiteSpace(CommandInput.Text)) return;

        string command = CommandInput.Text.Trim();
        ConsoleOutput.Text += $"> {command}\n";

        var (success, message) = await _nodeService.SendCommandAsync(_currentNode, _currentServer.RemoteServerId, command);
        
        if (success)
        {
            ConsoleOutput.Text += $"[系统] 命令已发送\n";
        }
        else
        {
            ConsoleOutput.Text += $"[错误] {message}\n";
        }

        CommandInput.Text = "";
        
        // 滚动到底部
        ConsoleOutput.Select(ConsoleOutput.Text.Length, 0);
    }

    private async void GiveOp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string playerName)
        {
            await SendCommandDirect($"op {playerName}");
        }
    }

    private async void KickPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string playerName)
        {
            await SendCommandDirect($"kick {playerName}");
        }
    }

    private async Task SendCommandDirect(string command)
    {
        if (_currentNode == null || _currentServer == null) return;
        
        await _nodeService.SendCommandAsync(_currentNode, _currentServer.RemoteServerId, command);
        ConsoleOutput.Text += $"> {command}\n";
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        var (success, message) = await _nodeService.CreateBackupAsync(_currentNode, _currentServer.RemoteServerId);
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (success)
        {
            ConsoleOutput.Text += $"[系统] 备份已开始\n";
            await Task.Delay(2000);
            await LoadBackups();
        }
        else
        {
            await ShowErrorDialog(message);
        }
    }

    private async Task LoadBackups()
    {
        if (_currentNode == null || _currentServer == null) return;

        try
        {
            var backups = await _nodeService.ListBackupsAsync(_currentNode, _currentServer.RemoteServerId);
            if (backups != null)
            {
                _backups.Clear();
                foreach (var backup in backups)
                {
                    _backups.Add(backup);
                }
            }

            NoBackupsText.Visibility = _backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BackupListView.Visibility = _backups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // 忽略错误
        }
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BackupInfo backup)
        {
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除备份 {backup.Name} 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                // TODO: 实现删除备份API
                await LoadBackups();
            }
        }
    }

    private async void GameSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        var content = await _nodeService.GetServerPropertiesAsync(_currentNode, _currentServer.RemoteServerId);
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (content != null)
        {
            var dialog = new PropertiesEditorDialog(content);
            dialog.XamlRoot = this.XamlRoot;
            
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var newContent = dialog.GetContent();
                LoadingOverlay.Visibility = Visibility.Visible;
                var (success, message) = await _nodeService.UpdateServerPropertiesAsync(_currentNode, _currentServer.RemoteServerId, newContent);
                LoadingOverlay.Visibility = Visibility.Collapsed;

                if (success)
                {
                    ConsoleOutput.Text += "[系统] 游戏设置已保存\n";
                }
                else
                {
                    await ShowErrorDialog(message);
                }
            }
        }
        else
        {
            await ShowErrorDialog("无法加载配置文件");
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;

        var dialog = new RemoteServerSettingsDialog(_currentNode, _currentServer);
        dialog.XamlRoot = this.XamlRoot;
        
        await dialog.ShowAsync();
        
        if (dialog.ServerDeleted)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
        else
        {
            await LoadServerInfo();
        }
    }

    private void Console_Click(object sender, RoutedEventArgs e)
    {
        // 当前页面已经是控制台
    }

    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        GameSettings_Click(sender, e);
    }

    private void Plugins_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 实现插件管理
        ConsoleOutput.Text += "[系统] 插件管理功能开发中\n";
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        CreateBackup_Click(sender, e);
    }

    private void FileManage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null || _currentServer == null) return;
        Frame.Navigate(typeof(FileManagerPage), (_currentNode, _currentServer.RemoteServerId));
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // WebSocket 连接
    private async Task ConnectWebSocket()
    {
        if (_currentNode == null || _currentServer == null) return;

        // 防止重复连接
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            return;
        }

        // 确保清理旧连接
        DisconnectWebSocket();

        try
        {
            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20); // 保持连接活跃
            _wsCts = new CancellationTokenSource();

            // 构建WebSocket URL
            var wsUrl = $"ws://{_currentNode.Host}:{_currentNode.Port}/ws/logs/{_currentServer.RemoteServerId}";
            
            // 添加认证Token
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_currentNode.Token}");

            await _webSocket.ConnectAsync(new Uri(wsUrl), _wsCts.Token);
            
            ConsoleOutput.Text += "[\u7cfb\u7edf] \u5df2\u8fde\u63a5\u5230\u670d\u52a1\u5668\u63a7\u5236\u53f0\n";

            // 开始接收消息
            _ = Task.Run(async () => await ReceiveWebSocketMessages());
        }
        catch (Exception ex)
        {
            ConsoleOutput.Text += $"[\u9519\u8bef] WebSocket \u8fde\u63a5\u5931\u8d25: {ex.Message}\n";
            DisconnectWebSocket();
        }
    }

    private void DisconnectWebSocket()
    {
        try
        {
            _wsCts?.Cancel();
            _webSocket?.Dispose();
            _webSocket = null;
            _wsCts = null;
        }
        catch { }
    }

    private void ParsePlayerEvent(string message)
    {
        // 解析玩家加入
        var joinMatch = Regex.Match(message, @"(\w+) joined the game");
        if (joinMatch.Success)
        {
            var name = joinMatch.Groups[1].Value;
            if (!_players.Contains(name))
            {
                DispatcherQueue.TryEnqueue(() => _players.Add(name));
            }
            return;
        }

        // 解析玩家离开
        var leaveMatch = Regex.Match(message, @"(\w+) left the game");
        if (leaveMatch.Success)
        {
            var name = leaveMatch.Groups[1].Value;
            if (_players.Contains(name))
            {
                DispatcherQueue.TryEnqueue(() => _players.Remove(name));
            }
        }
    }

    private async Task ReceiveWebSocketMessages()
    {
        if (_webSocket == null || _wsCts == null) return;
    
        var buffer = new byte[4096];
        try
        {
            while (_webSocket.State == WebSocketState.Open && !_wsCts.Token.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _wsCts.Token);
                    
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        
                    // 清理 ANSI 转义序列（颜色代码）
                    message = RemoveAnsiEscapeCodes(message);
                        
                    // 在UI线程上更新控制台
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ConsoleOutput.Text += message + "\n";
                            
                        // 自动滚动到底部
                        ConsoleOutput.Select(ConsoleOutput.Text.Length, 0);
                            
                        // 解析玩家事件
                        ParsePlayerEvent(message);
                    });
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ConsoleOutput.Text += $"[错误] WebSocket 连接断开: {ex.Message}\n";
            });
        }
    }
    
    // 清理 ANSI 转义序列（颜色代码）
    private string RemoveAnsiEscapeCodes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // 匹配 ANSI 转义序列: \x1b[...m 或 \u001b[...m
        // \x1b 和 \u001b 都是 ESC 字符 (ASCII 27)
        return Regex.Replace(text, @"\x1b\[[0-9;]*m|\u001b\[[0-9;]*m|\[\d+;\d+;\d+m|\[\d+m|\[0m", "");
    }

    private void CopyConsole_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = ConsoleOutput.SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
        {
            var package = new DataPackage();
            package.SetText(selectedText);
            Clipboard.SetContent(package);
        }
    }

    private void SelectAllConsole_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOutput.SelectAll();
    }

    private void AnalyzeSelected_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = ConsoleOutput.SelectedText;
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            // 如果没有选中内容，使用全部日志
            selectedText = ConsoleOutput.Text;
        }

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        // 导航到日志分析页面
        Frame.Navigate(typeof(LogAnalysisPage), selectedText);
    }
}

// 配置文件编辑器对话框
public sealed partial class PropertiesEditorDialog : ContentDialog
{
    private TextBox _textBox;

    public PropertiesEditorDialog(string content)
    {
        Title = "编辑 server.properties";
        PrimaryButtonText = "保存";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Primary;

        _textBox = new TextBox
        {
            Text = content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            Height = 400
        };

        Content = new ScrollViewer
        {
            Content = _textBox,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    public string GetContent() => _textBox.Text;
}
