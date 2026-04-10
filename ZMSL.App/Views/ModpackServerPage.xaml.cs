using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage.Pickers;
using ZMSL.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ZMSL.App.Views;

public sealed partial class ModpackServerPage : Page
{
    // CurseForge API
    private const string CurseForgeApiKey = "$2a$10$bL4bIL5pUWqfcO7KQtnMReakwtfHbNKh6v1uTpKlzhwoueEJQnPnm";
    private static HttpClient? _cfApiClient;
    private static HttpClient CfApiClient => _cfApiClient ??= CreateCfApiClient();

    private static HttpClient CreateCfApiClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.curseforge.com/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("x-api-key", CurseForgeApiKey);
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");
        return client;
    }

    private readonly ServerManagerService _serverManager;
    private readonly ObservableCollection<ModpackItem> _modpacks = new();
    private readonly ObservableCollection<ModpackVersionItem> _versions = new();
    private ModpackItem? _selectedModpack;
    private ModpackVersionItem? _selectedVersion;
    private string? _localFilePath;
    private bool _isFromCurseForge = true;

    // 分页
    private int _currentPage = 0;
    private int _totalHits = 0;
    private const int PageSize = 20;
    private string? _currentKeyword;

    // 下载控制
    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading = false;

    public ModpackServerPage()
    {
        this.InitializeComponent();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        ModpackList.ItemsSource = _modpacks;
        VersionComboBox.ItemsSource = _versions;
        EmptyPanel.Visibility = Visibility.Visible;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // 页面加载时自动刷新整合包列表
        await SearchModpacksAsync();
    }

    #region CurseForge搜索

    private async void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _currentPage = 0;
            _currentKeyword = SearchBox.Text;
            await SearchModpacksAsync();
        }
    }

    private async Task SearchModpacksAsync(bool resetPage = true)
    {
        try
        {
            LoadingRing.IsActive = true;
            EmptyPanel.Visibility = Visibility.Collapsed;

            if (resetPage)
            {
                _currentPage = 0;
            }

            _modpacks.Clear();

            // CurseForge API: gameId=432 (Minecraft), classId=4471 (Modpacks)
            var query = string.IsNullOrWhiteSpace(_currentKeyword) ? "" : Uri.EscapeDataString(_currentKeyword);
            var offset = _currentPage * PageSize;

            var url = $"v1/mods/search?gameId=432&classId=4471&pageSize={PageSize}&index={offset}&sortField=2&sortOrder=desc";
            if (!string.IsNullOrEmpty(query))
                url += $"&searchFilter={query}";

            var response = await CfApiClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CurseForgeSearchResult>(json);

                if (result?.Data != null)
                {
                    _totalHits = result.Pagination?.TotalCount ?? result.Data.Count;

                    foreach (var mod in result.Data)
                    {
                        var authors = mod.Authors?.Select(a => a.Name).FirstOrDefault() ?? "未知";
                        var gameVersions = mod.LatestFilesIndexes?
                            .Select(f => f.GameVersion)
                            .Where(v => !string.IsNullOrEmpty(v))
                            .Distinct()
                            .Take(5)
                            .ToList() ?? new List<string?>();

                        _modpacks.Add(new ModpackItem
                        {
                            ProjectId = mod.Id.ToString(),
                            Slug = mod.Slug ?? "",
                            Name = mod.Name ?? "",
                            Author = authors,
                            Description = mod.Summary ?? "",
                            IconUrl = mod.Logo?.Url ?? "",
                            Downloads = (int)mod.DownloadCount,
                            GameVersions = string.Join(", ", gameVersions)
                        });
                    }
                }

                UpdatePaginationUI();
                OnlineCountText.Text = $"({_totalHits})";
                EmptyPanel.Visibility = _modpacks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                Debug.WriteLine($"CurseForge API error: {response.StatusCode}");
                EmptyPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CurseForge search error: {ex.Message}");
            EmptyPanel.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void UpdatePaginationUI()
    {
        var totalPages = Math.Max(1, (_totalHits + PageSize - 1) / PageSize);
        var currentDisplay = _currentPage + 1;

        PageInfoText.Text = $"{currentDisplay} / {totalPages}";
        PrevPageBtn.IsEnabled = _currentPage > 0;
        NextPageBtn.IsEnabled = (_currentPage + 1) * PageSize < _totalHits;
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            await SearchModpacksAsync(resetPage: false);
        }
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_currentPage + 1) * PageSize < _totalHits)
        {
            _currentPage++;
            await SearchModpacksAsync(resetPage: false);
        }
    }

    #endregion

    #region 整合包选择

    private void ModpackItem_Click(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is ModpackItem modpack)
        {
            SelectModpack(modpack);
        }
    }

    private void SelectModpack_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ModpackItem modpack)
        {
            SelectModpack(modpack);
        }
    }

    private async void SelectModpack(ModpackItem modpack)
    {
        _selectedModpack = modpack;
        _selectedVersion = null;
        _isFromCurseForge = true;
        _localFilePath = null;
        _versions.Clear();

        VersionSelectPanel.Visibility = Visibility.Visible;
        VersionInfoText.Text = "正在获取版本列表...";

        // 获取所有页的版本文件信息
        try
        {
            var allFiles = new List<CurseForgeFile>();
            int pageIndex = 0;
            const int pageSize = 50;
            int totalCount = 0;

            // 循环获取所有页
            while (true)
            {
                var url = $"v1/mods/{modpack.ProjectId}/files?pageSize={pageSize}&index={pageIndex * pageSize}";
                var response = await CfApiClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    break;

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CurseForgeFilesResult>(json);

                if (result?.Data == null || result.Data.Count == 0)
                    break;

                allFiles.AddRange(result.Data);

                // 获取总数
                if (result.Pagination != null)
                {
                    totalCount = result.Pagination.TotalCount;
                }

                // 更新进度
                VersionInfoText.Text = $"正在获取版本列表... ({allFiles.Count}/{totalCount})";

                // 如果已经获取完所有数据，退出循环
                if (allFiles.Count >= totalCount || result.Data.Count < pageSize)
                    break;

                pageIndex++;

                // 防止无限循环，最多获取500个版本
                if (allFiles.Count >= 500)
                    break;
            }

            Debug.WriteLine($"共获取 {allFiles.Count} 个版本文件");

            if (allFiles.Count > 0)
            {
                // 方式1：查找有 serverPackFileId 的客户端文件（这些文件关联了服务端包）
                var filesWithServerPack = allFiles
                    .Where(f => f.ServerPackFileId.HasValue && f.ServerPackFileId.Value > 0)
                    .ToList();

                // 方式2：查找文件名包含 "Server" 的独立服务端文件（作为备选）
                var serverNamedFiles = allFiles
                    .Where(f => (f.FileName ?? "").Contains("Server", StringComparison.OrdinalIgnoreCase) ||
                                (f.DisplayName ?? "").Contains("Server", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                int serverVersionCount = 0;

                // 优先处理有 serverPackFileId 的文件
                if (filesWithServerPack.Count > 0)
                {
                    foreach (var file in filesWithServerPack)
                    {
                        _versions.Add(new ModpackVersionItem
                        {
                            FileId = file.ServerPackFileId!.Value,  // 使用服务端包的 FileId
                            ClientFileId = file.Id,  // 保存客户端文件 ID 以便参考
                            DisplayName = file.DisplayName ?? file.FileName ?? "",
                            FileName = file.FileName ?? "",
                            DownloadUrl = "",  // 需要单独获取服务端包的下载链接
                            GameVersions = file.GameVersions ?? new List<string>(),
                            IsServerPack = true
                        });
                    }
                    serverVersionCount = filesWithServerPack.Count;
                }

                // 如果没有找到有 serverPackFileId 的文件，则使用文件名匹配的方式
                if (_versions.Count == 0 && serverNamedFiles.Count > 0)
                {
                    foreach (var file in serverNamedFiles)
                    {
                        _versions.Add(new ModpackVersionItem
                        {
                            FileId = file.Id,
                            ClientFileId = file.Id,
                            DisplayName = file.DisplayName ?? file.FileName ?? "",
                            FileName = file.FileName ?? "",
                            DownloadUrl = file.DownloadUrl ?? "",
                            GameVersions = file.GameVersions ?? new List<string>(),
                            IsServerPack = true
                        });
                    }
                    serverVersionCount = serverNamedFiles.Count;
                }

                if (_versions.Count > 0)
                {
                    VersionInfoText.Text = $"找到 {serverVersionCount} 个服务端版本 (共 {allFiles.Count} 个版本)";

                    // 默认选择第一个
                    VersionComboBox.SelectedIndex = 0;
                }
                else
                {
                    // 没有找到服务端版本
                    VersionInfoText.Text = $"未找到服务端版本 (共 {allFiles.Count} 个版本)";
                    SelectedVersionText.Text = "";
                }
            }
            else
            {
                VersionInfoText.Text = "未找到可用版本";
                SelectedVersionText.Text = "";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取文件信息失败: {ex.Message}");
            VersionInfoText.Text = $"获取版本列表失败: {ex.Message}";
        }

        UpdateSelectedModpackUI();
        UpdateCreateButtonState();
    }

    private async void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VersionComboBox.SelectedItem is ModpackVersionItem version)
        {
            _selectedVersion = version;

            // 如果是服务端包，需要获取服务端包的下载链接
            if (version.IsServerPack && string.IsNullOrEmpty(version.DownloadUrl))
            {
                try
                {
                    var url = $"v1/mods/{_selectedModpack?.ProjectId}/files/{version.FileId}";
                    var response = await CfApiClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<CurseForgeFileResponse>(json);

                        if (result?.Data != null)
                        {
                            version.DownloadUrl = result.Data.DownloadUrl ?? "";
                            version.FileName = result.Data.FileName ?? "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"获取服务端包信息失败: {ex.Message}");
                }
            }

            // 更新选中的整合包信息
            if (_selectedModpack != null)
            {
                _selectedModpack.LatestVersionId = version.FileId.ToString();
                _selectedModpack.LatestVersionNumber = version.DisplayName;
                _selectedModpack.DownloadUrl = version.DownloadUrl;
                _selectedModpack.FileName = version.FileName;
                _selectedModpack.GameVersions = string.Join(", ", version.GameVersions);
            }

            var serverPackText = version.IsServerPack ? " (服务端包)" : "";
            SelectedVersionText.Text = $"文件: {version.FileName}{serverPackText}";
            UpdateCreateButtonState();
        }
    }

    private void UpdateSelectedModpackUI()
    {
        if (_selectedModpack != null)
        {
            SelectedModpackPanel.Visibility = Visibility.Visible;
            SelectedNameText.Text = _selectedModpack.Name;
            SelectedAuthorText.Text = _selectedModpack.Author;
            SelectedGameVersionText.Text = _selectedModpack.GameVersions;

            if (!string.IsNullOrEmpty(_selectedModpack.IconUrl))
            {
                SelectedIconImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_selectedModpack.IconUrl));
            }

            if (string.IsNullOrEmpty(ServerNameBox.Text))
            {
                ServerNameBox.Text = _selectedModpack.Name;
            }

            // 更新版本信息
            if (_selectedVersion != null)
            {
                SelectedVersionText.Text = $"文件: {_selectedVersion.FileName}";
            }
            else
            {
                SelectedVersionText.Text = "";
            }
        }
        else if (!string.IsNullOrEmpty(_localFilePath))
        {
            SelectedModpackPanel.Visibility = Visibility.Visible;
            VersionSelectPanel.Visibility = Visibility.Collapsed;
            var fileName = Path.GetFileNameWithoutExtension(_localFilePath);
            SelectedNameText.Text = fileName;
            SelectedAuthorText.Text = "本地文件";
            SelectedGameVersionText.Text = "";
            SelectedVersionText.Text = "";
            SelectedIconImage.Source = null;

            if (string.IsNullOrEmpty(ServerNameBox.Text))
            {
                ServerNameBox.Text = fileName;
            }
        }
        else
        {
            SelectedModpackPanel.Visibility = Visibility.Collapsed;
            VersionSelectPanel.Visibility = Visibility.Collapsed;
        }
    }

    #endregion

    #region 来源切换

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SourceRadio == null || LocalUploadPanel == null) return;

        _isFromCurseForge = SourceRadio.SelectedIndex == 0;
        LocalUploadPanel.Visibility = _isFromCurseForge ? Visibility.Collapsed : Visibility.Visible;

        if (!_isFromCurseForge)
        {
            _selectedModpack = null;
            _selectedVersion = null;
            _versions.Clear();
        }
        else
        {
            _localFilePath = null;
        }

        UpdateSelectedModpackUI();
        UpdateCreateButtonState();
    }

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".mrpack");
        picker.FileTypeFilter.Add(".zip");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _localFilePath = file.Path;
            LocalFilePathBox.Text = file.Path;
            _selectedModpack = null;
            UpdateSelectedModpackUI();
            UpdateCreateButtonState();
        }
    }

    #endregion

    #region 创建服务器

    private void UpdateCreateButtonState()
    {
        bool hasModpack;
        if (_isFromCurseForge)
        {
            // 需要选择整合包且选择了版本（或者版本列表为空但有下载链接）
            hasModpack = _selectedModpack != null &&
                         (_selectedVersion != null || !string.IsNullOrEmpty(_selectedModpack.DownloadUrl));
        }
        else
        {
            hasModpack = !string.IsNullOrEmpty(_localFilePath);
        }
        CreateButton.IsEnabled = hasModpack && !_isDownloading;
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        var serverName = ServerNameBox.Text.Trim();
        if (string.IsNullOrEmpty(serverName))
        {
            await ShowDialogAsync("错误", "请输入服务器名称");
            return;
        }

        // 检查服务器名称是否已存在
        if (await _serverManager.IsServerNameExistsAsync(serverName))
        {
            await ShowDialogAsync("错误", $"服务器名称 '{serverName}' 已存在，请使用其他名称");
            return;
        }

        var minMemory = (int)MinMemoryBox.Value;
        var maxMemory = (int)MaxMemoryBox.Value;
        var port = (int)PortBox.Value;
        var saveClientPack = SaveClientPackCheck.IsChecked == true;

        if (minMemory > maxMemory)
        {
            await ShowDialogAsync("错误", "最小内存不能大于最大内存");
            return;
        }

        CreateButton.IsEnabled = false;
        _isDownloading = true;

        try
        {
            string modpackPath;
            string modpackName;

            if (_isFromCurseForge && _selectedModpack != null)
            {
                // 从CurseForge下载
                if (string.IsNullOrEmpty(_selectedModpack.DownloadUrl))
                {
                    await ShowDialogAsync("错误", "无法获取整合包下载地址，该整合包可能不允许第三方下载");
                    CreateButton.IsEnabled = true;
                    _isDownloading = false;
                    return;
                }

                // 下载整合包
                var downloadResult = await DownloadModpackWithProgressAsync(_selectedModpack);
                if (downloadResult == null)
                {
                    CreateButton.IsEnabled = true;
                    _isDownloading = false;
                    return;
                }
                modpackPath = downloadResult;
                modpackName = _selectedModpack.Name;
            }
            else if (!string.IsNullOrEmpty(_localFilePath))
            {
                modpackPath = _localFilePath;
                modpackName = Path.GetFileNameWithoutExtension(_localFilePath);
            }
            else
            {
                await ShowDialogAsync("错误", "请选择整合包");
                CreateButton.IsEnabled = true;
                _isDownloading = false;
                return;
            }

            // 导航到核心选择页面
            var createParams = new ModpackCreateParams
            {
                ServerName = serverName,
                ModpackName = modpackName,
                ModpackPath = modpackPath,
                MinMemory = minMemory,
                MaxMemory = maxMemory,
                Port = port,
                SaveClientPack = saveClientPack
            };

            Frame.Navigate(typeof(ModpackCoreSelectPage), createParams);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"准备创建服务器失败: {ex.Message}");
            await ShowDialogAsync("错误", $"准备创建服务器失败: {ex.Message}");
        }
        finally
        {
            CreateButton.IsEnabled = true;
            _isDownloading = false;
        }
    }

    private async Task<string?> DownloadModpackWithProgressAsync(ModpackItem modpack)
    {
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        // 创建下载进度UI
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Width = 300,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var progressText = new TextBlock
        {
            Text = "准备下载...",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = $"正在下载 {modpack.Name}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var contentPanel = new StackPanel
        {
            Children = { statusText, progressBar, progressText }
        };

        // 使用 TaskCompletionSource 来控制对话框
        var downloadComplete = new TaskCompletionSource<bool>();
        string? resultPath = null;
        Exception? downloadException = null;

        var downloadDialog = new ContentDialog
        {
            Title = "下载整合包",
            Content = contentPanel,
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot,
            DefaultButton = ContentDialogButton.None
        };

        // 处理取消按钮
        downloadDialog.CloseButtonClick += (s, args) =>
        {
            _downloadCts?.Cancel();
            downloadComplete.TrySetResult(false);
        };

        // 启动下载任务
        var downloadTask = Task.Run(async () =>
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "ZMSL_Modpacks");
                Directory.CreateDirectory(tempDir);
                var filePath = Path.Combine(tempDir, modpack.FileName);

                using var downloadClient = new HttpClient { Timeout = TimeSpan.FromMinutes(60) };
                downloadClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");

                using var response = await downloadClient.GetAsync(modpack.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var totalBytesText = totalBytes > 0 ? FormatBytes(totalBytes) : "未知";

                using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long downloadedBytes = 0;
                int bytesRead;
                var lastUpdate = DateTime.Now;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    downloadedBytes += bytesRead;

                    // 每100ms更新一次UI
                    if ((DateTime.Now - lastUpdate).TotalMilliseconds > 100)
                    {
                        lastUpdate = DateTime.Now;
                        var downloadedText = FormatBytes(downloadedBytes);
                        var percent = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0;

                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (totalBytes > 0)
                            {
                                progressBar.Value = percent;
                                progressText.Text = $"{downloadedText} / {totalBytesText} ({percent:F1}%)";
                            }
                            else
                            {
                                progressBar.IsIndeterminate = true;
                                progressText.Text = $"已下载 {downloadedText}";
                            }
                        });
                    }
                }

                // 下载完成
                DispatcherQueue.TryEnqueue(() =>
                {
                    progressBar.Value = 100;
                    progressText.Text = "下载完成！";
                });

                resultPath = filePath;
                downloadComplete.TrySetResult(true);
            }
            catch (OperationCanceledException)
            {
                downloadComplete.TrySetResult(false);
            }
            catch (Exception ex)
            {
                downloadException = ex;
                downloadComplete.TrySetResult(false);
            }
        }, ct);

        // 显示对话框并等待
        var dialogTask = downloadDialog.ShowAsync();

        // 等待下载完成或取消
        var success = await downloadComplete.Task;

        // 关闭对话框
        downloadDialog.Hide();

        if (downloadException != null)
        {
            await ShowDialogAsync("下载失败", $"下载整合包失败: {downloadException.Message}");
            return null;
        }

        if (!success)
        {
            return null;
        }

        return resultPath;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    #endregion

    #region 导航

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _downloadCts?.Cancel();
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    #endregion

    #region 辅助方法

    private async Task ShowDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    #endregion
}

#region 整合包模型

public class ModpackItem : INotifyPropertyChanged
{
    private string _projectId = "";
    private string _slug = "";
    private string _name = "";
    private string _author = "";
    private string _description = "";
    private string _iconUrl = "";
    private int _downloads;
    private string _gameVersions = "";
    private string _latestVersionId = "";
    private string _latestVersionNumber = "";
    private string _downloadUrl = "";
    private string _fileName = "";

    public string ProjectId { get => _projectId; set { _projectId = value; OnPropertyChanged(); } }
    public string Slug { get => _slug; set { _slug = value; OnPropertyChanged(); } }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Author { get => _author; set { _author = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public string IconUrl { get => _iconUrl; set { _iconUrl = value; OnPropertyChanged(); } }
    public int Downloads { get => _downloads; set { _downloads = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadsText)); } }
    public string GameVersions { get => _gameVersions; set { _gameVersions = value; OnPropertyChanged(); } }
    public string LatestVersionId { get => _latestVersionId; set { _latestVersionId = value; OnPropertyChanged(); } }
    public string LatestVersionNumber { get => _latestVersionNumber; set { _latestVersionNumber = value; OnPropertyChanged(); } }
    public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(); } }
    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }

    public string DownloadsText => Downloads >= 1000000 ? $"{Downloads / 1000000.0:F1}M 下载" :
                                   Downloads >= 1000 ? $"{Downloads / 1000.0:F1}K 下载" :
                                   $"{Downloads} 下载";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ModpackVersionItem : INotifyPropertyChanged
{
    private int _fileId;
    private int _clientFileId;
    private string _displayName = "";
    private string _fileName = "";
    private string _downloadUrl = "";
    private List<string> _gameVersions = new();
    private bool _isServerPack;

    public int FileId { get => _fileId; set { _fileId = value; OnPropertyChanged(); } }
    public int ClientFileId { get => _clientFileId; set { _clientFileId = value; OnPropertyChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }
    public string DownloadUrl { get => _downloadUrl; set { _downloadUrl = value; OnPropertyChanged(); } }
    public List<string> GameVersions { get => _gameVersions; set { _gameVersions = value; OnPropertyChanged(); OnPropertyChanged(nameof(GameVersionsText)); } }
    public bool IsServerPack { get => _isServerPack; set { _isServerPack = value; OnPropertyChanged(); } }

    public string GameVersionsText => GameVersions.Count > 0 ? $"[{string.Join(", ", GameVersions.Take(3))}]" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

#endregion

#region CurseForge API 模型

public class CurseForgeSearchResult
{
    [JsonPropertyName("data")]
    public List<CurseForgeMod>? Data { get; set; }

    [JsonPropertyName("pagination")]
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgePagination
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }
}

public class CurseForgeMod
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("downloadCount")]
    public long DownloadCount { get; set; }

    [JsonPropertyName("logo")]
    public CurseForgeLogo? Logo { get; set; }

    [JsonPropertyName("authors")]
    public List<CurseForgeAuthor>? Authors { get; set; }

    [JsonPropertyName("latestFilesIndexes")]
    public List<CurseForgeFileIndex>? LatestFilesIndexes { get; set; }
}

public class CurseForgeLogo
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class CurseForgeAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class CurseForgeFileIndex
{
    [JsonPropertyName("gameVersion")]
    public string? GameVersion { get; set; }

    [JsonPropertyName("fileId")]
    public int FileId { get; set; }
}

public class CurseForgeFilesResult
{
    [JsonPropertyName("data")]
    public List<CurseForgeFile>? Data { get; set; }

    [JsonPropertyName("pagination")]
    public CurseForgePagination? Pagination { get; set; }
}

public class CurseForgeFileResponse
{
    [JsonPropertyName("data")]
    public CurseForgeFile? Data { get; set; }
}

public class CurseForgeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("gameVersions")]
    public List<string>? GameVersions { get; set; }

    [JsonPropertyName("serverPackFileId")]
    public int? ServerPackFileId { get; set; }
}

#endregion
