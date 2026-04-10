using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace ZMSL.App.Views;

public sealed partial class ServerSettingsDialog : ContentDialog
{
    private readonly ServerManagerService _serverManager;
    private readonly DatabaseService _db;
    private readonly LocalServer _server;
    private List<ZMSL.App.Models.JavaInfo> _javaList = new();
    private string? _pendingIconPath; // null=未更改, ""=清除, 其他=新路径
    private bool _isForgeOrNeoForge;

    public bool ServerDeleted { get; private set; }

    public ServerSettingsDialog(LocalServer server)
    {
        this.InitializeComponent();
        _server = server;
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _db = App.Services.GetRequiredService<DatabaseService>();

        // 判断是否为 Forge/NeoForge
        var coreTypeLower = server.CoreType?.ToLowerInvariant() ?? "";
        _isForgeOrNeoForge = coreTypeLower.Contains("forge") || coreTypeLower.Contains("neoforge");

        LoadServerInfo();
        this.Loaded += ServerSettingsDialog_Loaded;
    }

    private async void ServerSettingsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // 应用 Forge/NeoForge 只读保护
        if (_isForgeOrNeoForge)
        {
            // 核心类型只读（XAML 已设置 IsReadOnly="True"，这里补充提示栏）
            ForgeReadOnlyInfo.IsOpen = true;
            // 核心文件不可更换
            ChangeJarButton.IsEnabled = false;
            ForgeCoreReadOnlyInfo.IsOpen = true;
        }

        await LoadJavaListAsync();
    }

    private void LoadServerInfo()
    {
        ServerNameBox.Text = _server.Name;
        EnglishAliasBox.Text = _server.EnglishAlias;
        McVersionBox.Text = _server.MinecraftVersion;
        CoreTypeBox.Text = _server.CoreType;
        JarFileBox.Text = _server.JarFileName;
        JarPathText.Text = Path.Combine(_server.ServerPath, _server.JarFileName);
        JavaPathText.Text = _server.JavaPath ?? "使用系统默认Java";
        MinMemoryBox.Text = _server.MinMemoryMB.ToString();
        MaxMemoryBox.Text = _server.MaxMemoryMB.ToString();
        JvmArgsBox.Text = _server.JvmArgs ?? "";
        ServerPortBox.Text = _server.Port.ToString();
        StartupCommandBox.Text = _server.StartupCommand ?? "";
        ServerPathBox.Text = _server.ServerPath;

        // 加载 authlib 配置
        EnableAuthlibSwitch.IsOn = _server.EnableAuthlib;
        AuthlibUrlBox.Text = _server.AuthlibUrl ?? "littleskin.cn";
        AuthlibPanel.Visibility = _server.EnableAuthlib ? Visibility.Visible : Visibility.Collapsed;

        // 加载自定义图标：优先显示服务器目录下的 server-icon.png
        var serverIconPath = Path.Combine(_server.ServerPath, "server-icon.png");
        var iconToShow = File.Exists(serverIconPath) ? serverIconPath : _server.IconPath;
        LoadIconPreview(iconToShow);
    }

    /// <summary>
    /// 加载图标预览
    /// </summary>
    private void LoadIconPreview(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            try
            {
                var uri = new Uri(iconPath);
                CustomIconImage.Source = new BitmapImage(uri);
                CustomIconImage.Visibility = Visibility.Visible;
                DefaultIconGlyph.Visibility = Visibility.Collapsed;
                ClearIconButton.Visibility = Visibility.Visible;
            }
            catch
            {
                ShowDefaultIcon();
            }
        }
        else
        {
            ShowDefaultIcon();
        }
    }

    private void ShowDefaultIcon()
    {
        CustomIconImage.Source = null;
        CustomIconImage.Visibility = Visibility.Collapsed;
        DefaultIconGlyph.Visibility = Visibility.Visible;
        ClearIconButton.Visibility = Visibility.Collapsed;
    }

    private async void SelectIcon_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".ico");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _pendingIconPath = file.Path;
            LoadIconPreview(file.Path);
        }
    }

    private void ClearIcon_Click(object sender, RoutedEventArgs e)
    {
        _pendingIconPath = ""; // 空字符串表示清除
        ShowDefaultIcon();
    }

    private async Task LoadJavaListAsync()
    {
        try
        {
            var javaList = await _db.ExecuteWithLockAsync(async context =>
                await context.JavaInstallations
                    .Where(j => j.IsValid)
                    .OrderByDescending(j => j.Version)
                    .ToListAsync());

            this.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (JavaComboBox == null || JavaComboBox.XamlRoot == null) return;

                    if (javaList != null)
                    {
                        _javaList = javaList;

                        if (!string.IsNullOrEmpty(_server.JavaPath) &&
                            !_javaList.Any(j => j.Path == _server.JavaPath))
                        {
                            _javaList.Insert(0, new ZMSL.App.Models.JavaInfo
                            {
                                Path = _server.JavaPath,
                                Version = 0,
                                Source = "Custom",
                                IsValid = true,
                                DetectedAt = System.DateTime.Now
                            });
                        }

                        JavaComboBox.ItemsSource = _javaList;
                        JavaComboBox.DisplayMemberPath = "Path";

                        if (!string.IsNullOrEmpty(_server.JavaPath))
                        {
                            var selected = _javaList.FirstOrDefault(j => j.Path == _server.JavaPath);
                            if (selected != null)
                                JavaComboBox.SelectedItem = selected;
                        }
                    }
                }
                catch (System.Exception) { }
            });
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Error loading Java list: {ex.Message}");
        }
    }

    private void JavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaComboBox.SelectedItem is ZMSL.App.Models.JavaInfo java)
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
        // Forge/NeoForge 不允许更换核心
        if (_isForgeOrNeoForge) return;

        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".jar");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            JarFileBox.Text = file.Name;
            JarPathText.Text = file.Path;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_server.ServerPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _server.ServerPath,
                UseShellExecute = true
            });
        }
    }

    private void EnableAuthlibSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        AuthlibPanel.Visibility = EnableAuthlibSwitch.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        var xamlRoot = this.XamlRoot;
        this.Hide();

        var confirm = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除服务器 \"{_server.Name}\" 吗？\n\n注意：这只会删除数据库记录，不会删除服务器文件。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot
        };

        if (await confirm.ShowAsync() == ContentDialogResult.Primary)
        {
            await _serverManager.DeleteServerAsync(_server.Id);
            ServerDeleted = true;
        }
    }

    private async void Save_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            _server.Name = ServerNameBox.Text;
            _server.EnglishAlias = EnglishAliasBox.Text;
            _server.MinecraftVersion = McVersionBox.Text;

            // Forge/NeoForge：不修改核心类型和 JAR 文件名
            if (!_isForgeOrNeoForge)
            {
                _server.CoreType = CoreTypeBox.Text;
                _server.JarFileName = JarFileBox.Text;
            }

            if (JavaComboBox.SelectedItem is ZMSL.App.Models.JavaInfo java)
                _server.JavaPath = java.Path;

            if (int.TryParse(MinMemoryBox.Text, out var minMem))
                _server.MinMemoryMB = minMem;
            if (int.TryParse(MaxMemoryBox.Text, out var maxMem))
                _server.MaxMemoryMB = maxMem;

            _server.JvmArgs = JvmArgsBox.Text;

            if (int.TryParse(ServerPortBox.Text, out var port))
                _server.Port = port;

            // Forge/NeoForge：不覆盖启动命令（保留原有命令）
            if (!_isForgeOrNeoForge)
            {
                _server.StartupCommand = StartupCommandBox.Text;
            }

            // 保存 authlib 配置
            _server.EnableAuthlib = EnableAuthlibSwitch.IsOn;
            _server.AuthlibUrl = AuthlibUrlBox.Text.Trim();

            if (_server.EnableAuthlib && !_server.AuthlibDownloaded)
            {
                await DownloadAuthlibAsync();
            }

            // 处理图标：将图片复制/转换为服务器目录下的 server-icon.png (64×64)
            if (_pendingIconPath != null)
            {
                if (string.IsNullOrEmpty(_pendingIconPath))
                {
                    // 清除图标：删除 server-icon.png 并清空数据库记录
                    var existingIcon = Path.Combine(_server.ServerPath, "server-icon.png");
                    if (File.Exists(existingIcon))
                    {
                        try { File.Delete(existingIcon); } catch { }
                    }
                    _server.IconPath = null;
                }
                else
                {
                    // 将选中图片缩放为 64×64 并保存为 server-icon.png
                    var destPath = Path.Combine(_server.ServerPath, "server-icon.png");
                    var success = await ConvertToServerIconAsync(_pendingIconPath, destPath);
                    if (success)
                        _server.IconPath = destPath;
                }
            }

            await _serverManager.UpdateServerAsync(_server);
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// 将任意图片缩放为 64×64 的 PNG 并保存到目标路径。
    /// 使用 Windows.Graphics.Imaging，无需额外依赖。
    /// </summary>
    private static async Task<bool> ConvertToServerIconAsync(string sourcePath, string destPath)
    {
        try
        {
            var sourceFile = await StorageFile.GetFileFromPathAsync(sourcePath);
            using var sourceStream = await sourceFile.OpenReadAsync();

            var decoder = await BitmapDecoder.CreateAsync(sourceStream);

            // 创建目标文件（覆盖已有文件）
            var destFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(destPath)!);
            var destFile = await destFolder.CreateFileAsync(
                Path.GetFileName(destPath),
                CreationCollisionOption.ReplaceExisting);

            using var destStream = await destFile.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateForTranscodingAsync(destStream, decoder);

            // 缩放为 64×64
            encoder.BitmapTransform.ScaledWidth = 64;
            encoder.BitmapTransform.ScaledHeight = 64;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;

            // 强制输出为 PNG
            var pngEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destStream);
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform { ScaledWidth = 64, ScaledHeight = 64, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            pngEncoder.SetPixelData(
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Premultiplied,
                64, 64,
                decoder.DpiX, decoder.DpiY,
                pixelData.DetachPixelData());

            await pngEncoder.FlushAsync();

            System.Diagnostics.Debug.WriteLine($"[ServerIcon] 已生成 server-icon.png: {destPath}");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerIcon] 生成 server-icon.png 失败: {ex.Message}");
            return false;
        }
    }

    private async Task DownloadAuthlibAsync()
    {
        try
        {
            var authlibPath = Path.Combine(_server.ServerPath, "authlib-injector-1.2.7.jar");

            if (File.Exists(authlibPath))
            {
                _server.AuthlibDownloaded = true;
                return;
            }

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);

            var url = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/55/authlib-injector-1.2.7.jar";
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(authlibPath, content);

            _server.AuthlibDownloaded = true;
            System.Diagnostics.Debug.WriteLine($"authlib-injector 下载完成: {authlibPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"下载 authlib-injector 失败: {ex.Message}");
        }
    }
}
