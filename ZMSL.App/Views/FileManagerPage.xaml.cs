using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class FileManagerPage : Page
{
    private readonly LinuxNodeService? _nodeService;
    private string _serverPath = "";
    private bool _isRemote;
    private LinuxNode? _node;
    private string? _remoteServerId;
    private string _currentPath = "";
    private ObservableCollection<FileItemViewModel> _files = new();

    public FileManagerPage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
        FileListView.ItemsSource = _files;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string localPath)
        {
            // 本地文件管理
            _serverPath = localPath;
            _isRemote = false;
            TitleText.Text = "本地文件管理";
            await LoadFiles();
        }
        else if (e.Parameter is (LinuxNode node, string serverId))
        {
            // 远程文件管理
            _node = node;
            _remoteServerId = serverId;
            _isRemote = true;
            TitleText.Text = $"远程文件管理 - {node.Name}";
            await LoadFiles();
        }
        else
        {
            Frame.GoBack();
        }
    }

    private async Task LoadFiles()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        _files.Clear();

        try
        {
            if (_isRemote)
            {
                await LoadRemoteFiles();
            }
            else
            {
                await LoadLocalFiles();
            }

            PathText.Text = string.IsNullOrEmpty(_currentPath) ? "根目录" : _currentPath;
            EmptyHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "错误",
                Content = $"加载文件列表失败: {ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async Task LoadLocalFiles()
    {
        var targetPath = Path.Combine(_serverPath, _currentPath);
        var dir = new DirectoryInfo(targetPath);

        if (!dir.Exists) return;

        await Task.Run(() =>
        {
            var dirs = dir.GetDirectories();
            var files = dir.GetFiles();

            DispatcherQueue.TryEnqueue(() =>
            {
                foreach (var d in dirs.OrderBy(x => x.Name))
                {
                    _files.Add(new FileItemViewModel
                    {
                        Name = d.Name,
                        IsDirectory = true,
                        SizeText = "<文件夹>",
                        TypeText = "文件夹",
                        IconGlyph = "\uE8B7"  // 文件夹图标
                    });
                }

                foreach (var f in files.OrderBy(x => x.Name))
                {
                    _files.Add(new FileItemViewModel
                    {
                        Name = f.Name,
                        IsDirectory = false,
                        SizeText = FormatFileSize(f.Length),
                        TypeText = f.Extension,
                        IconGlyph = GetFileIcon(f.Extension)
                    });
                }
            });
        });
    }

    private async Task LoadRemoteFiles()
    {
        if (_node == null || string.IsNullOrEmpty(_remoteServerId) || _nodeService == null)
            return;

        var fileList = await _nodeService.ListFilesAsync(_node, _remoteServerId, _currentPath);

        if (fileList != null)
        {
            foreach (var file in fileList.OrderBy(f => !f.IsDirectory).ThenBy(f => f.Name))
            {
                _files.Add(new FileItemViewModel
                {
                    Name = file.Name,
                    IsDirectory = file.IsDirectory,
                    SizeText = file.IsDirectory ? "<文件夹>" : FormatFileSize(file.Size),
                    TypeText = file.IsDirectory ? "文件夹" : Path.GetExtension(file.Name),
                    IconGlyph = file.IsDirectory ? "\uE8B7" : GetFileIcon(Path.GetExtension(file.Name))
                });
            }
        }
    }

    private async void FileList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FileItemViewModel fileInfo)
        {
            if (fileInfo.IsDirectory)
            {
                // 双击文件夹，进入目录
                if (_isRemote)
                {
                    // 远程路径使用正斜杠
                    _currentPath = string.IsNullOrEmpty(_currentPath)
                        ? fileInfo.Name
                        : $"{_currentPath}/{fileInfo.Name}";
                }
                else
                {
                    // 本地路径使用系统分隔符
                    _currentPath = string.IsNullOrEmpty(_currentPath)
                        ? fileInfo.Name
                        : Path.Combine(_currentPath, fileInfo.Name);
                }
                await LoadFiles();
            }
            else
            {
                // 双击文件，打开编辑器
                OpenFileEditor(fileInfo.Name);
            }
        }
    }

    private void OpenFileEditor(string fileName)
    {
        if (_isRemote)
        {
            // 远程文件
            if (_node == null || string.IsNullOrEmpty(_remoteServerId)) return;

            // 远程路径使用正斜杠
            var filePath = string.IsNullOrEmpty(_currentPath)
                ? fileName
                : $"{_currentPath}/{fileName}";

            Frame.Navigate(typeof(FileEditorPage), (_node, _remoteServerId, filePath));
        }
        else
        {
            // 本地文件
            var filePath = Path.Combine(_serverPath, _currentPath, fileName);
            Frame.Navigate(typeof(FileEditorPage), filePath);
        }
    }

    private async void NavigateUp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentPath)) return;

        if (_isRemote)
        {
            // 远程路径使用正斜杠
            var lastSlash = _currentPath.LastIndexOf('/');
            _currentPath = lastSlash > 0 ? _currentPath.Substring(0, lastSlash) : "";
        }
        else
        {
            // 本地路径使用系统分隔符
            var parent = Path.GetDirectoryName(_currentPath);
            _currentPath = parent ?? "";
        }

        await LoadFiles();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadFiles();
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 创建文件选择器
            var picker = new Windows.Storage.Pickers.FileOpenPicker();

            // 获取窗口句柄
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            // 设置文件类型过滤器
            picker.FileTypeFilter.Add("*");

            // 选择文件
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            LoadingOverlay.Visibility = Visibility.Visible;

            if (_isRemote)
            {
                // 远程文件上传
                if (_node == null || string.IsNullOrEmpty(_remoteServerId))
                {
                    throw new Exception("远程服务器信息无效");
                }

                // 构建目标路径（远程路径使用正斜杠）
                var targetPath = string.IsNullOrEmpty(_currentPath)
                    ? file.Name
                    : $"{_currentPath}/{file.Name}";

                // 上传文件
                var (success, message) = await _nodeService!.UploadFileAsync(_node, _remoteServerId, file.Path);

                if (!success)
                {
                    throw new Exception(message);
                }

                // 显示成功消息
                var successDialog = new ContentDialog
                {
                    Title = "成功",
                    Content = "文件上传成功",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            else
            {
                // 本地文件复制
                var targetPath = Path.Combine(_serverPath, _currentPath, file.Name);
                var targetDir = Path.GetDirectoryName(targetPath);

                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                await file.CopyAsync(
                    await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(targetPath)!),
                    file.Name,
                    Windows.Storage.NameCollisionOption.ReplaceExisting
                );

                // 显示成功消息
                var successDialog = new ContentDialog
                {
                    Title = "成功",
                    Content = "文件复制成功",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await successDialog.ShowAsync();
            }

            // 刷新文件列表
            await LoadFiles();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "错误",
                Content = $"上传文件失败: {ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private string GetFileIcon(string extension)
    {
        return extension.ToLower() switch
        {
            ".jar" => "\uE8B5",      // 应用图标
            ".zip" or ".rar" or ".7z" => "\uE8B7",  // 压缩包
            ".txt" or ".log" => "\uE8A5",  // 文本
            ".json" or ".yml" or ".yaml" or ".properties" => "\uE943",  // 配置
            ".png" or ".jpg" or ".jpeg" or ".gif" => "\uEB9F",  // 图片
            _ => "\uE8A5"  // 默认文件图标
        };
    }
}

public class FileItemViewModel
{
    public string Name { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string SizeText { get; set; } = "";
    public string TypeText { get; set; } = "";
    public string IconGlyph { get; set; } = "\uE8A5";
}
