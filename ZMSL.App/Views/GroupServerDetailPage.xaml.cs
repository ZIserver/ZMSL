using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using ZMSL.App.Models;
using ZMSL.App.Services;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views;

public sealed partial class GroupServerDetailPage : Page
{
    public GroupServerDetailViewModel ViewModel { get; }
    private Microsoft.UI.Xaml.DispatcherTimer? _resourceTimer;
    private readonly ServerManagerService _serverManager;
    
    private List<CommandSuggestion> _allCommands = new();
    private List<CommandSuggestion> _filteredSuggestions = new();

    public GroupServerDetailPage()
    {
        this.InitializeComponent();
        ViewModel = ActivatorUtilities.CreateInstance<GroupServerDetailViewModel>(App.Services);
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        
        InitializeCommands();
    }

    private void InitializeCommands()
    {
        // Velocity proxy commands
        _allCommands = new List<CommandSuggestion>
        {
            new("/server {server}", "Switch to a specific server", false),
            new("/glist", "List players on all servers", false),
            new("/alert {message}", "Broadcast message to all servers", false),
            new("/send {player} {server}", "Send player to a server", true),
            new("/find {player}", "Find which server a player is on", true),
            new("/kick {player} {reason}", "Kick player from proxy", true),
            new("/shutdown", "Shutdown the proxy", false),
            new("/end", "Shutdown the proxy", false),
            new("/velocity reload", "Reload configuration", false),
            new("/velocity version", "Show version info", false),
            new("/velocity plugins", "Show plugins", false)
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string serverId)
        {
            await ViewModel.InitializeAsync(serverId);
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Start resource timer
        _resourceTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _resourceTimer.Tick += ResourceTimer_Tick;
        _resourceTimer.Start();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _resourceTimer?.Stop();
    }

    private void ResourceTimer_Tick(object? sender, object e)
    {
        if (ViewModel.Server != null && ViewModel.IsRunning)
        {
            var process = _serverManager.GetServerProcess(ViewModel.Server.Id);
            if (process != null)
            {
                ViewModel.UpdateResourceUsageCommand.Execute(process);
            }
        }
        else
        {
            ViewModel.CpuUsage = 0;
            ViewModel.MemoryUsage = 0;
            ViewModel.MemoryUsageText = "0 MB";
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        // 固定返回到群组服务器列表页面，避免返回到创建向导等中间页面
        Frame.Navigate(typeof(GroupServerListPage));
    }

    // Console Logic
    private void ConsoleOutput_SelectionChanged(object sender, RoutedEventArgs e)
    {
        // Optional: Handle selection
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

    // Command Logic
    private async void CommandInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            if (!string.IsNullOrWhiteSpace(CommandInput.Text))
            {
                await ViewModel.SendCommand(CommandInput.Text);
                CommandSuggestionPopup.IsOpen = false;
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Tab && _filteredSuggestions.Count > 0)
        {
            e.Handled = true;
            var suggestion = _filteredSuggestions[0];
            ApplySuggestion(suggestion);
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CommandSuggestionPopup.IsOpen = false;
        }
    }

    private void CommandInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var input = CommandInput.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(input) || !input.StartsWith("/"))
        {
            CommandSuggestionPopup.IsOpen = false;
            return;
        }
        
        _filteredSuggestions = _allCommands
            .Where(c => c.Command.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
        
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
            var transform = CommandInput.TransformToVisual(null);
            var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            
            // Simplified positioning logic (Always try above first)
            CommandSuggestionPopup.HorizontalOffset = point.X;
            // Approximate height or calculate dynamically
            double popupHeight = Math.Min(_filteredSuggestions.Count * 56 + 10, 300);
            CommandSuggestionPopup.VerticalOffset = point.Y - popupHeight - 4;
            
            CommandSuggestionPopup.IsOpen = true;
        }
        catch { }
    }

    private void CommandInput_LostFocus(object sender, RoutedEventArgs e)
    {
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
            ApplySuggestion(suggestion);
        }
    }

    private void ApplySuggestion(CommandSuggestion suggestion)
    {
        // Simple replace for now
        CommandInput.Text = suggestion.Command.Split(' ')[0] + " ";
        CommandSuggestionPopup.IsOpen = false;
        CommandInput.SelectionStart = CommandInput.Text.Length;
        CommandInput.Focus(FocusState.Programmatic);
    }

    // Sub-server Management
    private void AddSubServer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddSubServerCommand.Execute(null);
    }

    private void RemoveSubServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SubServerItem item)
        {
            ViewModel.RemoveSubServerCommand.Execute(item);
        }
    }

    private async void ManageSubServers_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAvailableServersCommand.ExecuteAsync(null);
        SubServerSelectionDialog.XamlRoot = this.XamlRoot;
        await SubServerSelectionDialog.ShowAsync();
    }

    private void ServerItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is SelectableServerItem item)
        {
            item.IsSelected = !item.IsSelected;
        }
    }

    private void SubServerSelectionDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ViewModel.ConfirmServerSelectionCommand.Execute(null);
    }
    
    private void SetMainLobby_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is SelectableServerItem item)
        {
             ViewModel.SetAsMainLobbyCommand.Execute(item);
        }
    }

    private void ManageFiles_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Server != null)
        {
            Frame.Navigate(typeof(FileManagerPage), ViewModel.Server.ServerPath);
        }
    }

    private void ManagePlugins_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Server != null)
        {
            Frame.Navigate(typeof(PluginManagerPage), ViewModel.Server);
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        // Show advanced settings or full config editor if needed
        // For now, the Right Panel handles basic settings.
        // Maybe open the config file directly?
        if (ViewModel.Server != null)
        {
            var configPath = Path.Combine(ViewModel.Server.ServerPath, "velocity.toml");
            if (File.Exists(configPath))
            {
                try 
                {
                    await Task.Run(() => 
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = configPath,
                            UseShellExecute = true
                        });
                    });
                }
                catch { }
            }
        }
    }

    private async void ShowConfigDialog_Click(object sender, RoutedEventArgs e)
    {
        // Reload config to ensure we have the latest values from file
        await ViewModel.LoadConfigAsync();

        var content = new StackPanel { Spacing = 16, Width = 400 };
        
        // Bind Port
        var portBox = new NumberBox 
        { 
            Header = "绑定端口 (Bind Port)",
            Minimum = 1024, 
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = ViewModel.BindPort,
            PlaceholderText = "25565"
        };
        // Binding manually or just update VM on close? Let's use simple binding or assignment.
        // For simplicity in code-behind, we'll read values on PrimaryButtonClick.
        
        var motdBox = new TextBox
        {
            Header = "MOTD (服务器简介)",
            Text = ViewModel.Motd
        };
        
        var maxPlayersBox = new NumberBox
        {
            Header = "最大玩家数",
            Minimum = 1,
            Value = ViewModel.MaxPlayers,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
        };
        
        var onlineModeSwitch = new ToggleSwitch
        {
            Header = "正版验证 (Online Mode)",
            IsOn = ViewModel.OnlineMode,
            OffContent = "关闭",
            OnContent = "开启"
        };
        
        var forceKeySwitch = new ToggleSwitch
        {
            Header = "强制密钥验证 (Force Key Auth)",
            IsOn = ViewModel.ForceKeyAuthentication,
            OffContent = "关闭",
            OnContent = "开启"
        };
        
        var showMaxPlayersSwitch = new ToggleSwitch
        {
            Header = "显示最大玩家数",
            IsOn = ViewModel.ShowMaxPlayers,
            OffContent = "关闭",
            OnContent = "开启"
        };

        content.Children.Add(portBox);
        content.Children.Add(motdBox);
        content.Children.Add(maxPlayersBox);
        content.Children.Add(onlineModeSwitch);
        content.Children.Add(forceKeySwitch);
        content.Children.Add(showMaxPlayersSwitch);

        var dialog = new ContentDialog
        {
            Title = "群组服务器设置",
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Update ViewModel
            // If value is NaN (empty), default to 25565
            ViewModel.BindPort = double.IsNaN(portBox.Value) ? 25565 : (int)portBox.Value;
            ViewModel.Motd = motdBox.Text;
            ViewModel.MaxPlayers = double.IsNaN(maxPlayersBox.Value) ? 500 : (int)maxPlayersBox.Value;
            ViewModel.OnlineMode = onlineModeSwitch.IsOn;
            ViewModel.ForceKeyAuthentication = forceKeySwitch.IsOn;
            ViewModel.ShowMaxPlayers = showMaxPlayersSwitch.IsOn;
            
            // Save to file
            ViewModel.SaveConfigCommand.Execute(null);
        }
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
