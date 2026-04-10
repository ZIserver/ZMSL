using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class FileEditorPage : Page
{
    private readonly LinuxNodeService? _nodeService;
    private string _localFilePath = "";
    private bool _isRemote;
    private LinuxNode? _node;
    private string? _remoteServerId;
    private string? _remoteFilePath;

    public FileEditorPage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string localPath)
        {
            // 本地文件
            _localFilePath = localPath;
            _isRemote = false;
            FileNameText.Text = Path.GetFileName(localPath);
            FilePathText.Text = localPath;
            await LoadLocalFile();
        }
        else if (e.Parameter is (LinuxNode node, string serverId, string filePath))
        {
            // 远程文件
            _node = node;
            _remoteServerId = serverId;
            _remoteFilePath = filePath;
            _isRemote = true;
            FileNameText.Text = Path.GetFileName(filePath);
            FilePathText.Text = $"{node.Name} - {filePath}";
            await LoadRemoteFile();
        }
        else
        {
            Frame.GoBack();
        }
    }

    private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CharCountText.Text = $"{ContentTextBox.Text.Length} 字符";
    }

    private async Task LoadLocalFile()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "正在加载...";
        
        try
        {
            if (File.Exists(_localFilePath))
            {
                var content = await File.ReadAllTextAsync(_localFilePath);
                ContentTextBox.Text = content;
                CharCountText.Text = $"{content.Length} 字符";
                StatusText.Text = "文件已加载";
            }
            else
            {
                StatusText.Text = "文件不存在";
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载文件失败: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadRemoteFile()
    {
        if (_node == null || string.IsNullOrEmpty(_remoteServerId) || 
            string.IsNullOrEmpty(_remoteFilePath) || _nodeService == null)
            return;

        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "正在加载...";
        
        try
        {
            var content = await _nodeService.ReadFileAsync(_node, _remoteServerId, _remoteFilePath);
            if (content != null)
            {
                ContentTextBox.Text = content;
                CharCountText.Text = $"{content.Length} 字符";
                StatusText.Text = "文件已加载";
            }
            else
            {
                await ShowErrorDialog("无法读取远程文件");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载远程文件失败: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "正在保存...";

        try
        {
            if (_isRemote)
            {
                await SaveRemoteFile();
            }
            else
            {
                await SaveLocalFile();
            }

            StatusText.Text = "保存成功";
            
            // 短暂延迟后显示提示
            await Task.Delay(1500);
            StatusText.Text = "就绪";
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"保存失败: {ex.Message}");
            StatusText.Text = "保存失败";
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task SaveLocalFile()
    {
        await File.WriteAllTextAsync(_localFilePath, ContentTextBox.Text, Encoding.UTF8);
    }

    private async Task SaveRemoteFile()
    {
        if (_node == null || string.IsNullOrEmpty(_remoteServerId) || 
            string.IsNullOrEmpty(_remoteFilePath) || _nodeService == null)
            return;

        var success = await _nodeService.WriteFileAsync(_node, _remoteServerId, 
            _remoteFilePath, ContentTextBox.Text);
        
        if (!success)
        {
            throw new Exception("保存远程文件失败");
        }
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
}
