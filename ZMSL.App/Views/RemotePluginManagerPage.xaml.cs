using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Windows.Storage.Pickers;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.Views;

public sealed partial class RemotePluginManagerPage : Page
{
    // HttpClient 延迟初始化 (与本地插件管理共享)
    private static HttpClient? _apiClient;
    private static HttpClient? _downloadClient;

    private static HttpClient ApiClient => _apiClient ??= CreateApiClient();
    private static HttpClient DownloadClient => _downloadClient ??= CreateDownloadClient();

    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.modrinth.com/v2/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");
        return client;
    }

    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0");
        return client;
    }

    private readonly LinuxNodeService _nodeService;
    private LinuxNode? _node;
    private RemoteServer? _server;
    private bool _isInstalling = false;

    private readonly ObservableCollection<RemoteLocalPluginItem> _allLocalPlugins = new();
    private readonly ObservableCollection<RemoteLocalPluginItem> _filteredLocalPlugins = new();
    private readonly ObservableCollection<RemoteModrinthPluginItem> _onlinePlugins = new();

    // 分页相关
    private int _currentPage = 0;
    private int _totalHits = 0;
    private const int PageSize = 20;
    private string? _currentKeyword;

    // 本地插件信息 (slug/名称 -> 版本)
    private readonly Dictionary<string, string> _localPluginVersions = new(StringComparer.OrdinalIgnoreCase);

    public RemotePluginManagerPage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
        LocalPluginList.ItemsSource = _filteredLocalPlugins;
        OnlinePluginList.ItemsSource = _onlinePlugins;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (LinuxNode node, RemoteServer server))
        {
            _node = node;
            _server = server;
            ServerNameText.Text = $"{server.Name} (MC {server.MinecraftVersion}) @ {node.Name}";
            LoadAll();
        }
    }

    private async void LoadAll()
    {
        await LoadLocalPluginsAsync();
        ParseLocalPluginVersions();
        _currentPage = 0;
        _currentKeyword = null;
        await LoadModrinthPluginsAsync();
    }

    #region 本地插件版本解析

    private void ParseLocalPluginVersions()
    {
        _localPluginVersions.Clear();

        var loaderTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bukkit", "paper", "spigot", "purpur", "folia",
            "fabric", "forge", "neoforge", "quilt", "sponge",
            "velocity", "bungeecord", "waterfall", "bungee"
        };

        foreach (var plugin in _allLocalPlugins)
        {
            var fileName = plugin.FileName.Replace(".disabled", "");
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var (pluginName, version) = ParsePluginFileName(baseName, loaderTypes);

            if (!string.IsNullOrEmpty(pluginName) && !string.IsNullOrEmpty(version))
            {
                var slug = pluginName.ToLower().Replace(" ", "-").Replace("_", "-");
                _localPluginVersions[slug] = version;
                _localPluginVersions[pluginName] = version;
                _localPluginVersions[pluginName.ToLower()] = version;
            }
        }
    }

    private static (string name, string version) ParsePluginFileName(string baseName, HashSet<string> loaderTypes)
    {
        var parts = baseName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count < 2) return (baseName, "");

        int versionIndex = -1;
        string version = "";

        for (int i = parts.Count - 1; i >= 1; i--)
        {
            var part = parts[i];
            if (loaderTypes.Contains(part)) continue;
            if (part.Equals("SNAPSHOT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("RELEASE", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("BETA", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("ALPHA", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsVersionLike(part))
            {
                versionIndex = i;
                version = part.TrimStart('v', 'V');
                break;
            }
        }

        if (versionIndex <= 0) return (baseName, "");

        var nameParts = new List<string>();
        for (int i = 0; i < versionIndex; i++)
        {
            if (!loaderTypes.Contains(parts[i]))
            {
                nameParts.Add(parts[i]);
            }
        }

        var pluginName = string.Join("-", nameParts);
        return (pluginName, version);
    }

    private static bool IsVersionLike(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        var str = s.TrimStart('v', 'V');
        if (string.IsNullOrEmpty(str)) return false;
        if (!char.IsDigit(str[0])) return false;
        return Regex.IsMatch(str, @"^\d+\.\d+");
    }

    private string? FindLocalVersion(string slug, string name)
    {
        if (_localPluginVersions.TryGetValue(slug, out var ver1)) return ver1;
        if (_localPluginVersions.TryGetValue(name, out var ver2)) return ver2;
        return null;
    }

    private static int CompareVersions(string v1, string v2)
    {
        if (string.IsNullOrEmpty(v1) && string.IsNullOrEmpty(v2)) return 0;
        if (string.IsNullOrEmpty(v1)) return -1;
        if (string.IsNullOrEmpty(v2)) return 1;

        var regex = new Regex(@"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        var m1 = regex.Match(v1);
        var m2 = regex.Match(v2);

        if (!m1.Success && !m2.Success) return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
        if (!m1.Success) return -1;
        if (!m2.Success) return 1;

        var major1 = int.Parse(m1.Groups[1].Value);
        var major2 = int.Parse(m2.Groups[1].Value);
        if (major1 != major2) return major1.CompareTo(major2);

        var minor1 = m1.Groups[2].Success ? int.Parse(m1.Groups[2].Value) : 0;
        var minor2 = m2.Groups[2].Success ? int.Parse(m2.Groups[2].Value) : 0;
        if (minor1 != minor2) return minor1.CompareTo(minor2);

        var patch1 = m1.Groups[3].Success ? int.Parse(m1.Groups[3].Value) : 0;
        var patch2 = m2.Groups[3].Success ? int.Parse(m2.Groups[3].Value) : 0;
        return patch1.CompareTo(patch2);
    }

    #endregion

    #region 本地插件

    private async Task LoadLocalPluginsAsync()
    {
        if (_node == null || _server == null) return;

        LocalLoadingRing.IsActive = true;
        LocalEmptyPanel.Visibility = Visibility.Collapsed;
        _allLocalPlugins.Clear();

        try
        {
            var result = await _nodeService.ListServerPluginsEnhancedAsync(_node, _server.RemoteServerId);

            if (result != null)
            {
                foreach (var plugin in result.Plugins)
                {
                    var isEnabled = !plugin.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var displayName = plugin.Name.Replace(".disabled", "").Replace(".jar", "");

                    _allLocalPlugins.Add(new RemoteLocalPluginItem
                    {
                        FileName = plugin.Name,
                        DisplayName = displayName,
                        Size = plugin.Size,
                        ModTime = plugin.ModTime,
                        IsEnabled = isEnabled,
                        StatusBrush = isEnabled ? new SolidColorBrush(Colors.LimeGreen) : new SolidColorBrush(Colors.Orange),
                        Type = "plugin"
                    });
                }

                // 也加载 mods（如果是 Fabric/Forge 服务器）
                foreach (var mod in result.Mods)
                {
                    var isEnabled = !mod.Name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    var displayName = mod.Name.Replace(".disabled", "").Replace(".jar", "");

                    _allLocalPlugins.Add(new RemoteLocalPluginItem
                    {
                        FileName = mod.Name,
                        DisplayName = displayName,
                        Size = mod.Size,
                        ModTime = mod.ModTime,
                        IsEnabled = isEnabled,
                        StatusBrush = isEnabled ? new SolidColorBrush(Colors.LimeGreen) : new SolidColorBrush(Colors.Orange),
                        Type = "mod"
                    });
                }
            }

            ApplyLocalFilter();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载远程插件列表失败: {ex.Message}");
        }
        finally
        {
            LocalLoadingRing.IsActive = false;
        }
    }

    private void ApplyLocalFilter()
    {
        if (LocalEmptyPanel == null || InstalledCountText == null) return;

        var filterIndex = LocalFilterCombo?.SelectedIndex ?? 0;
        _filteredLocalPlugins.Clear();

        foreach (var p in _allLocalPlugins.OrderBy(x => x.DisplayName))
        {
            bool show = filterIndex switch
            {
                1 => p.IsEnabled,
                2 => !p.IsEnabled,
                _ => true
            };
            if (show) _filteredLocalPlugins.Add(p);
        }

        InstalledCountText.Text = $"({_allLocalPlugins.Count})";
        LocalEmptyPanel.Visibility = _filteredLocalPlugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LocalFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LocalEmptyPanel == null || InstalledCountText == null) return;
        ApplyLocalFilter();
    }

    private async void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_node == null || _server == null) return;

        if (sender is Button btn && btn.DataContext is RemoteLocalPluginItem plugin)
        {
            var confirm = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除 \"{plugin.DisplayName}\" 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                var (success, message) = await _nodeService.DeleteServerPluginAsync(
                    _node, _server.RemoteServerId, plugin.FileName, plugin.Type);

                if (success)
                {
                    _allLocalPlugins.Remove(plugin);
                    _filteredLocalPlugins.Remove(plugin);
                    ApplyLocalFilter();
                    ParseLocalPluginVersions();
                }
                else
                {
                    await ShowDialogAsync("删除失败", message);
                }
            }
        }
    }

    private async void UploadPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_node == null || _server == null) return;

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".jar");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        // 询问上传类型
        var dialog = new ContentDialog
        {
            Title = "选择类型",
            Content = "请选择要上传的文件类型：",
            PrimaryButtonText = "插件 (Plugin)",
            SecondaryButtonText = "模组 (Mod)",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;

        var type = result == ContentDialogResult.Primary ? "plugin" : "mod";

        var (success, message) = await _nodeService.UploadPluginAsync(_node, _server.RemoteServerId, file.Path, type);

        if (success)
        {
            await ShowDialogAsync("上传成功", message);
            await LoadLocalPluginsAsync();
            ParseLocalPluginVersions();
        }
        else
        {
            await ShowDialogAsync("上传失败", message);
        }
    }

    #endregion

    #region Modrinth在线插件

    private async Task LoadModrinthPluginsAsync(string? keyword = null, bool resetPage = true)
    {
        if (_server == null) return;

        try
        {
            OnlineLoadingRing.IsActive = true;
            OnlineEmptyPanel.Visibility = Visibility.Collapsed;

            if (resetPage)
            {
                _currentPage = 0;
                _currentKeyword = keyword;
            }

            _onlinePlugins.Clear();

            var mcVersion = _server.MinecraftVersion;
            var loaderType = GetLoaderType(_server.CoreType);

            var facets = new List<string>();
            facets.Add("[\"project_type:plugin\"]");

            if (!string.IsNullOrEmpty(mcVersion))
                facets.Add($"[\"versions:{mcVersion}\"]");

            if (!string.IsNullOrEmpty(loaderType))
                facets.Add($"[\"categories:{loaderType}\"]");

            var facetsParam = Uri.EscapeDataString($"[{string.Join(",", facets)}]");
            var query = string.IsNullOrWhiteSpace(_currentKeyword) ? "" : Uri.EscapeDataString(_currentKeyword);
            var offset = _currentPage * PageSize;

            var url = $"search?facets={facetsParam}&limit={PageSize}&offset={offset}&index=relevance";
            if (!string.IsNullOrEmpty(query))
                url += $"&query={query}";

            var response = await ApiClient.GetAsync(url);
            OnlineLoadingRing.IsActive = false;

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ModrinthSearchResult>(json);

                if (result?.Hits != null)
                {
                    _totalHits = result.TotalHits;

                    foreach (var hit in result.Hits)
                    {
                        var slug = hit.Slug ?? "";
                        var name = hit.Title ?? "";
                        var latestVersion = hit.LatestVersion ?? "";

                        string installText = "安装";
                        bool canInstall = true;

                        var localVersion = FindLocalVersion(slug, name);
                        if (localVersion != null)
                        {
                            var comparison = CompareVersions(localVersion, latestVersion);
                            if (comparison >= 0)
                            {
                                installText = "已安装";
                                canInstall = false;
                            }
                            else
                            {
                                installText = $"更新 ({localVersion})";
                                canInstall = true;
                            }
                        }

                        _onlinePlugins.Add(new RemoteModrinthPluginItem
                        {
                            ProjectId = hit.ProjectId ?? "",
                            Slug = slug,
                            Name = hit.Title ?? "",
                            Author = hit.Author ?? "未知",
                            Description = hit.Description ?? "",
                            IconUrl = hit.IconUrl ?? "",
                            Downloads = hit.Downloads,
                            Versions = string.Join(", ", hit.Versions?.TakeLast(5) ?? []),
                            LatestVersion = latestVersion,
                            InstallText = installText,
                            CanInstall = canInstall
                        });
                    }
                }

                UpdatePaginationUI();
                OnlineEmptyPanel.Visibility = _onlinePlugins.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                OnlineEmptyPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Modrinth API error: {ex.Message}");
            OnlineLoadingRing.IsActive = false;
            OnlineEmptyPanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePaginationUI()
    {
        var totalPages = (_totalHits + PageSize - 1) / PageSize;
        var currentDisplay = _currentPage + 1;

        OnlineCountText.Text = $"({_totalHits})";
        PageInfoText.Text = $"{currentDisplay} / {totalPages}";

        PrevPageBtn.IsEnabled = _currentPage > 0;
        NextPageBtn.IsEnabled = (_currentPage + 1) * PageSize < _totalHits;
    }

    private async void PrevPage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            await LoadModrinthPluginsAsync(_currentKeyword, resetPage: false);
        }
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_currentPage + 1) * PageSize < _totalHits)
        {
            _currentPage++;
            await LoadModrinthPluginsAsync(_currentKeyword, resetPage: false);
        }
    }

    private static string GetLoaderType(string? coreType)
    {
        return coreType?.ToLower() switch
        {
            "paper" or "spigot" or "bukkit" => "bukkit",
            "purpur" => "purpur",
            "folia" => "folia",
            "sponge" => "sponge",
            "fabric" => "fabric",
            "forge" => "forge",
            "neoforge" => "neoforge",
            "quilt" => "quilt",
            "velocity" => "velocity",
            "bungeecord" or "waterfall" => "bungeecord",
            _ => "bukkit"
        };
    }

    private void OnlineSearch_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = LoadModrinthPluginsAsync(OnlineSearchBox.Text);
        }
    }

    private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (_node == null || _server == null) return;
        if (sender is not Button btn || btn.DataContext is not RemoteModrinthPluginItem plugin) return;
        if (!plugin.CanInstall || _isInstalling) return;

        _isInstalling = true;

        try
        {
            btn.IsEnabled = false;
            plugin.InstallText = "获取版本...";
            plugin.CanInstall = false;

            var mcVersion = _server.MinecraftVersion;
            var loaderType = GetLoaderType(_server.CoreType);

            var versionsUrl = $"project/{plugin.ProjectId}/version?game_versions=[\"{mcVersion}\"]&loaders=[\"{loaderType}\"]";

            HttpResponseMessage response;
            try
            {
                response = await ApiClient.GetAsync(versionsUrl);
            }
            catch (TaskCanceledException)
            {
                plugin.InstallText = "安装";
                plugin.CanInstall = true;
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                plugin.InstallText = "失败";
                plugin.CanInstall = true;
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var versions = JsonSerializer.Deserialize<List<ModrinthVersion>>(json);

            if (versions == null || versions.Count == 0)
            {
                plugin.InstallText = "无版本";
                plugin.CanInstall = true;
                return;
            }

            var latestVersion = versions[0];
            var primaryFile = latestVersion.Files?.FirstOrDefault(f => f.Primary == true)
                              ?? latestVersion.Files?.FirstOrDefault();

            if (primaryFile?.Url == null)
            {
                plugin.InstallText = "失败";
                plugin.CanInstall = true;
                return;
            }

            plugin.InstallText = "下载中...";

            // 通过 Node API 下载到远程服务器
            var fileName = primaryFile.Filename ?? $"{plugin.Slug}.jar";
            var (success, message, taskId) = await _nodeService.DownloadPluginFromURLAsync(
                _node, _server.RemoteServerId, primaryFile.Url, fileName, "plugin");

            if (success)
            {
                plugin.InstallText = "已安装 ✓";
                plugin.CanInstall = false;
                plugin.LatestVersion = latestVersion.VersionNumber ?? "";

                // 刷新本地插件列表
                await LoadLocalPluginsAsync();
                ParseLocalPluginVersions();
            }
            else
            {
                plugin.InstallText = "失败";
                plugin.CanInstall = true;
                await ShowDialogAsync("安装失败", message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Install error: {ex.Message}");
            plugin.InstallText = "失败";
            plugin.CanInstall = true;
        }
        finally
        {
            btn.IsEnabled = true;
            _isInstalling = false;
        }
    }

    #endregion

    #region 通用

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadAll();
    }

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

#region 远程本地插件模型

public class RemoteLocalPluginItem : INotifyPropertyChanged
{
    private string _fileName = "";
    private string _displayName = "";
    private long _size;
    private string _modTime = "";
    private bool _isEnabled;
    private Brush _statusBrush = new SolidColorBrush(Colors.Gray);
    private string _type = "plugin";

    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    public long Size { get => _size; set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); } }
    public string ModTime { get => _modTime; set { _modTime = value; OnPropertyChanged(); } }
    public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    public Brush StatusBrush { get => _statusBrush; set { _statusBrush = value; OnPropertyChanged(); } }
    public string Type { get => _type; set { _type = value; OnPropertyChanged(); } }

    public string SizeText => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:F1} KB",
        _ => $"{Size / 1024.0 / 1024.0:F1} MB"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

#endregion

#region 远程Modrinth模型

public class RemoteModrinthPluginItem : INotifyPropertyChanged
{
    private string _projectId = "";
    private string _slug = "";
    private string _name = "";
    private string _author = "";
    private string _description = "";
    private string _iconUrl = "";
    private int _downloads;
    private string _versions = "";
    private string _latestVersion = "";
    private string _installText = "安装";
    private bool _canInstall = true;

    public string ProjectId { get => _projectId; set { _projectId = value; OnPropertyChanged(); } }
    public string Slug { get => _slug; set { _slug = value; OnPropertyChanged(); } }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Author { get => _author; set { _author = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public string IconUrl { get => _iconUrl; set { _iconUrl = value; OnPropertyChanged(); } }
    public int Downloads { get => _downloads; set { _downloads = value; OnPropertyChanged(); } }
    public string DownloadsText => Downloads >= 1000000 ? $"{Downloads / 1000000.0:F1}M" : Downloads >= 1000 ? $"{Downloads / 1000.0:F1}K" : Downloads.ToString();
    public string Versions { get => _versions; set { _versions = value; OnPropertyChanged(); } }
    public string LatestVersion { get => _latestVersion; set { _latestVersion = value; OnPropertyChanged(); } }
    public string InstallText { get => _installText; set { _installText = value; OnPropertyChanged(); } }
    public bool CanInstall { get => _canInstall; set { _canInstall = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

#endregion
