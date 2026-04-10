using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.Views;

public sealed partial class RemoteServerSettingsDialog : ContentDialog
{
    private readonly LinuxNodeService _nodeService;
    private readonly LinuxNode _node;
    private readonly RemoteServer _server;
    private List<NodeJavaInfo> _javaList = new();

    public bool ServerDeleted { get; private set; }

    public RemoteServerSettingsDialog(LinuxNode node, RemoteServer server)
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
        _node = node;
        _server = server;

        LoadServerInfo();
        _ = LoadJavaListAsync();
    }

    private void LoadServerInfo()
    {
        ServerNameBox.Text = _server.Name;
        McVersionBox.Text = _server.MinecraftVersion;
        CoreTypeBox.Text = _server.CoreType;
        JarFileBox.Text = _server.JarFileName;
        JavaPathText.Text = _server.JavaPath;
        MinMemoryBox.Text = _server.MinMemoryMB.ToString();
        MaxMemoryBox.Text = _server.MaxMemoryMB.ToString();
        JvmArgsBox.Text = _server.JvmArgs;
        ServerPortBox.Text = _server.Port.ToString();
        ServerIdBox.Text = _server.RemoteServerId;
    }

    private async Task LoadJavaListAsync()
    {
        try
        {
            var list = await _nodeService.ListJavaAsync(_node);
            if (list != null)
            {
                _javaList = list;
                JavaComboBox.ItemsSource = _javaList;

                if (!string.IsNullOrEmpty(_server.JavaPath))
                {
                    var current = _javaList.FirstOrDefault(j => j.Path == _server.JavaPath);
                    if (current != null)
                        JavaComboBox.SelectedItem = current;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载 Java 列表失败: {ex.Message}");
        }
    }

    private void JavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaComboBox.SelectedItem is NodeJavaInfo java)
        {
            JavaPathText.Text = java.Path;
        }
    }

    private async void DetectJava_Click(object sender, RoutedEventArgs e)
    {
        await LoadJavaListAsync();
    }

    private async void ChangeJar_Click(object sender, RoutedEventArgs e)
    {
        // 隐藏当前对话框以显示新对话框
        this.Hide();

        try
        {
            var files = await _nodeService.ListFilesAsync(_node, _server.RemoteServerId, "");
            var jars = files?.Where(f => !f.IsDirectory && f.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();

            var listView = new ListView
            {
                ItemsSource = jars,
                DisplayMemberPath = "Name",
                SelectionMode = ListViewSelectionMode.Single
            };

            var pickerDialog = new ContentDialog
            {
                Title = "选择启动核心",
                Content = listView,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            if (await pickerDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (listView.SelectedItem is RemoteFileInfo selectedFile)
                {
                    JarFileBox.Text = selectedFile.Name;
                }
            }
        }
        catch (Exception ex)
        {
            // 忽略错误
            System.Diagnostics.Debug.WriteLine($"获取文件列表失败: {ex.Message}");
        }
        finally
        {
            // 重新显示当前对话框
            await this.ShowAsync();
        }
    }

    private async void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        this.Hide();

        var confirm = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除服务器 \"{_server.Name}\" 吗？\n\n注意：这将永久删除服务器文件，不可恢复！",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            var (success, message) = await _nodeService.DeleteRemoteServerAsync(_node, _server.RemoteServerId);
            if (success)
            {
                ServerDeleted = true;
                // 删除成功，不需要再次显示设置对话框
            }
            else
            {
                var errorDialog = new ContentDialog
                {
                    Title = "删除失败",
                    Content = message,
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
                await this.ShowAsync(); // 重新显示设置对话框
            }
        }
        else
        {
            await this.ShowAsync(); // 用户取消删除，重新显示设置对话框
        }
    }

    private async void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            // 更新对象
            _server.Name = ServerNameBox.Text;
            _server.MinecraftVersion = McVersionBox.Text;
            _server.CoreType = CoreTypeBox.Text;
            _server.JarFileName = JarFileBox.Text;
            
            if (JavaComboBox.SelectedItem is NodeJavaInfo java)
                _server.JavaPath = java.Path;
            else if (!string.IsNullOrEmpty(JavaPathText.Text))
                _server.JavaPath = JavaPathText.Text; // 允许保留原有路径或手动输入

            if (int.TryParse(MinMemoryBox.Text, out var minMem))
                _server.MinMemoryMB = minMem;
            if (int.TryParse(MaxMemoryBox.Text, out var maxMem))
                _server.MaxMemoryMB = maxMem;
            
            _server.JvmArgs = JvmArgsBox.Text ?? "";
            
            if (int.TryParse(ServerPortBox.Text, out var port))
                _server.Port = port;

            var (success, message) = await _nodeService.UpdateServerConfigAsync(_node, _server);
            
            if (!success)
            {
                args.Cancel = true; // 保持对话框打开
                // 显示错误提示 (这里简单处理，实际可以使用 InfoBar 或 ToolTip)
                sender.Content = new StackPanel 
                { 
                    Children = 
                    { 
                        new TextBlock { Text = $"保存失败: {message}", Foreground = Microsoft.UI.Colors.Red.ToBrush() },
                        (UIElement)sender.Content 
                    } 
                };
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            System.Diagnostics.Debug.WriteLine($"保存失败: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    }
}

public static class ColorExtensions
{
    public static Microsoft.UI.Xaml.Media.SolidColorBrush ToBrush(this Color color)
    {
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(color);
    }
}
