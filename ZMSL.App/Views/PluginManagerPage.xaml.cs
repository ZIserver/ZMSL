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
using ZMSL.App.Models;

namespace ZMSL.App.Views;

public sealed partial class PluginManagerPage : Page
{
    // HttpClient 延迟初始化
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

    private LocalServer? _server;
    private bool _isInstalling = false;  // 安装锁，防止并发
    private readonly ObservableCollection<LocalPluginItem> _allLocalPlugins = new();
    private readonly ObservableCollection<LocalPluginItem> _filteredLocalPlugins = new();
    private readonly ObservableCollection<ModrinthPluginItem> _onlinePlugins = new();
    
    // 分页相关
    private int _currentPage = 0;
    private int _totalHits = 0;
    private const int PageSize = 20;
    private string? _currentKeyword;
    
    // 本地插件信息 (slug/名称 -> 版本)
    private readonly Dictionary<string, string> _localPluginVersions = new(StringComparer.OrdinalIgnoreCase);

    public PluginManagerPage()
    {
        this.InitializeComponent();
        LocalPluginList.ItemsSource = _filteredLocalPlugins;
        OnlinePluginList.ItemsSource = _onlinePlugins;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LocalServer server)
        {
            _server = server;
            ServerNameText.Text = $"{server.Name} (MC {server.MinecraftVersion})";
            LoadAll();
        }
    }

    private async void LoadAll()
    {
        LoadLocalPlugins();
        ParseLocalPluginVersions();  // 从文件名解析版本
        _currentPage = 0;
        _currentKeyword = null;
        await LoadModrinthPluginsAsync();
    }
    
    /// <summary>
    /// 从本地插件文件名解析插件名和版本
    /// 支持格式: 
    ///   - PluginName-1.2.3.jar
    ///   - PluginName-bukkit-1.2.3.jar  
    ///   - plugin-name-1.2.3-bukkit.jar
    ///   - PluginName-1.2.3-SNAPSHOT.jar
    /// </summary>
    private void ParseLocalPluginVersions()
    {
        _localPluginVersions.Clear();
        
        // 核心类型列表
        var loaderTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bukkit", "paper", "spigot", "purpur", "folia", 
            "fabric", "forge", "neoforge", "quilt", "sponge",
            "velocity", "bungeecord", "waterfall", "bungee"
        };
        
        foreach (var plugin in _allLocalPlugins)
        {
            var fileName = plugin.FileName.Replace(".disabled", "");
            // 移除 .jar 后缀
            var baseName = Path.GetFileNameWithoutExtension(fileName);
            
            // 尝试解析
            var (pluginName, version) = ParsePluginFileName(baseName, loaderTypes);
            
            if (!string.IsNullOrEmpty(pluginName) && !string.IsNullOrEmpty(version))
            {
                // 存储多种可能的名称格式
                var slug = pluginName.ToLower().Replace(" ", "-").Replace("_", "-");
                _localPluginVersions[slug] = version;
                _localPluginVersions[pluginName] = version;
                _localPluginVersions[pluginName.ToLower()] = version;
            }
        }
    }
    
    /// <summary>
    /// 解析单个插件文件名，返回 (插件名, 版本号)
    /// </summary>
    private static (string name, string version) ParsePluginFileName(string baseName, HashSet<string> loaderTypes)
    {
        // 用 - 或 _ 分割
        var parts = baseName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count < 2) return (baseName, "");
        
        // 从后向前查找版本号
        int versionIndex = -1;
        string version = "";
        
        for (int i = parts.Count - 1; i >= 1; i--)
        {
            var part = parts[i];
            // 跳过核心类型
            if (loaderTypes.Contains(part)) continue;
            // 跳过常见后缀 (SNAPSHOT, RELEASE, etc)
            if (part.Equals("SNAPSHOT", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("RELEASE", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("BETA", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("ALPHA", StringComparison.OrdinalIgnoreCase))
            {
                // 这些是版本后缀，继续向前找
                continue;
            }
            
            // 检查是否是版本号 (以数字开头，或 v+数字)
            if (IsVersionLike(part))
            {
                versionIndex = i;
                version = part.TrimStart('v', 'V');
                break;
            }
        }
        
        if (versionIndex <= 0) return (baseName, "");
        
        // 插件名是版本号之前的部分，排除核心类型
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
    
    /// <summary>
    /// 检查字符串是否像版本号
    /// </summary>
    private static bool IsVersionLike(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        
        // 移除 v 前缀
        var str = s.TrimStart('v', 'V');
        if (string.IsNullOrEmpty(str)) return false;
        
        // 必须以数字开头
        if (!char.IsDigit(str[0])) return false;
        
        // 检查是否包含点号分隔的数字 (1.2 或 1.2.3 或 1.2.3.4)
        return Regex.IsMatch(str, @"^\d+\.\d+");
    }
    
    /// <summary>
    /// 检查本地是否已安装某个插件，返回本地版本号（完全精确匹配）
    /// </summary>
    private string? FindLocalVersion(string slug, string name)
    {
        // 只做完全精确匹配
        if (_localPluginVersions.TryGetValue(slug, out var ver1)) return ver1;
        if (_localPluginVersions.TryGetValue(name, out var ver2)) return ver2;
        return null;
    }
    
    /// <summary>
    /// 比较两个版本号
    /// </summary>
    /// <returns>负数: v1 < v2, 0: 相等, 正数: v1 > v2</returns>
    private static int CompareVersions(string v1, string v2)
    {
        if (string.IsNullOrEmpty(v1) && string.IsNullOrEmpty(v2)) return 0;
        if (string.IsNullOrEmpty(v1)) return -1;
        if (string.IsNullOrEmpty(v2)) return 1;
        
        // 提取数字部分
        var regex = new Regex(@"^v?(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        var m1 = regex.Match(v1);
        var m2 = regex.Match(v2);
        
        if (!m1.Success && !m2.Success) return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
        if (!m1.Success) return -1;
        if (!m2.Success) return 1;
        
        // 比较主版本号
        var major1 = int.Parse(m1.Groups[1].Value);
        var major2 = int.Parse(m2.Groups[1].Value);
        if (major1 != major2) return major1.CompareTo(major2);
        
        // 比较次版本号
        var minor1 = m1.Groups[2].Success ? int.Parse(m1.Groups[2].Value) : 0;
        var minor2 = m2.Groups[2].Success ? int.Parse(m2.Groups[2].Value) : 0;
        if (minor1 != minor2) return minor1.CompareTo(minor2);
        
        // 比较修订版本号
        var patch1 = m1.Groups[3].Success ? int.Parse(m1.Groups[3].Value) : 0;
        var patch2 = m2.Groups[3].Success ? int.Parse(m2.Groups[3].Value) : 0;
        return patch1.CompareTo(patch2);
    }

    #region 本地插件

    private void LoadLocalPlugins()
    {
        if (_server == null) return;

        _allLocalPlugins.Clear();

        var pluginsDir = Path.Combine(_server.ServerPath, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            Directory.CreateDirectory(pluginsDir);
            ApplyLocalFilter();
            return;
        }

        foreach (var file in Directory.GetFiles(pluginsDir, "*.jar"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;

            _allLocalPlugins.Add(new LocalPluginItem
            {
                FileName = fileName,
                DisplayName = Path.GetFileNameWithoutExtension(file),
                FullPath = file,
                IsEnabled = true,
                StatusBrush = new SolidColorBrush(Colors.LimeGreen),
                ActionText = "禁用"
            });
        }

        foreach (var file in Directory.GetFiles(pluginsDir, "*.jar.disabled"))
        {
            var fileName = Path.GetFileName(file);
            var originalName = fileName.Replace(".disabled", "");

            _allLocalPlugins.Add(new LocalPluginItem
            {
                FileName = fileName,
                DisplayName = Path.GetFileNameWithoutExtension(originalName),
                FullPath = file,
                IsEnabled = false,
                StatusBrush = new SolidColorBrush(Colors.Orange),
                ActionText = "启用"
            });
        }

        ApplyLocalFilter();
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

    private async void TogglePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LocalPluginItem plugin)
        {
            try
            {
                if (plugin.IsEnabled)
                {
                    var newPath = plugin.FullPath + ".disabled";
                    File.Move(plugin.FullPath, newPath);
                    plugin.FullPath = newPath;
                    plugin.FileName = Path.GetFileName(newPath);
                    plugin.IsEnabled = false;
                    plugin.StatusBrush = new SolidColorBrush(Colors.Orange);
                    plugin.ActionText = "启用";
                }
                else
                {
                    var newPath = plugin.FullPath.Replace(".disabled", "");
                    File.Move(plugin.FullPath, newPath);
                    plugin.FullPath = newPath;
                    plugin.FileName = Path.GetFileName(newPath);
                    plugin.IsEnabled = true;
                    plugin.StatusBrush = new SolidColorBrush(Colors.LimeGreen);
                    plugin.ActionText = "禁用";
                }
                ApplyLocalFilter();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("错误", ex.Message);
            }
        }
    }

    private async void DeletePlugin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LocalPluginItem plugin)
        {
            var confirm = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除插件 \"{plugin.DisplayName}\" 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    File.Delete(plugin.FullPath);
                    _allLocalPlugins.Remove(plugin);
                    _filteredLocalPlugins.Remove(plugin);
                    ApplyLocalFilter();
                }
                catch (Exception ex)
                {
                    await ShowDialogAsync("删除失败", ex.Message);
                }
            }
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

            // 构建Modrinth搜索请求
            var facets = new List<string>();
            facets.Add("[\"project_type:plugin\"]");
            
            if (!string.IsNullOrEmpty(mcVersion))
                facets.Add($"[\"versions:{mcVersion}\"]");

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
                        
                        // 检测安装状态
                        string installText = "安装";
                        bool canInstall = true;
                        
                        var localVersion = FindLocalVersion(slug, name);
                        if (localVersion != null)
                        {
                            // 比较版本
                            var comparison = CompareVersions(localVersion, latestVersion);
                            if (comparison >= 0)
                            {
                                // 本地版本 >= 网络版本
                                installText = "已安装";
                                canInstall = false;
                            }
                            else
                            {
                                // 本地版本 < 网络版本，有更新
                                installText = $"更新 ({localVersion})";
                                canInstall = true;
                            }
                        }
                        
                        _onlinePlugins.Add(new ModrinthPluginItem
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

    private static string GetLoaderType(string coreType)
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
        if (_server == null) return;
        if (sender is not Button btn || btn.DataContext is not ModrinthPluginItem plugin) return;
        if (!plugin.CanInstall || _isInstalling) return;

        _isInstalling = true;
        
        try
        {
            btn.IsEnabled = false;
            plugin.InstallText = "获取版本...";
            plugin.CanInstall = false;

            var mcVersion = _server.MinecraftVersion;
            var loaderType = GetLoaderType(_server.CoreType);

            // 获取版本信息
            var versionsUrl = $"project/{plugin.ProjectId}/version?loaders=[\"{loaderType}\"]";
            if (!string.IsNullOrEmpty(mcVersion) && loaderType != "velocity")
            {
                versionsUrl += $"&game_versions=[\"{mcVersion}\"]";
            }
            
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

            // 下载文件
            var pluginsDir = Path.Combine(_server.ServerPath, "plugins");
            if (!Directory.Exists(pluginsDir))
                Directory.CreateDirectory(pluginsDir);

            var fileName = primaryFile.Filename ?? $"{plugin.Slug}.jar";
            var targetPath = Path.Combine(pluginsDir, fileName);

            try
            {
                using var downloadResponse = await DownloadClient.GetAsync(primaryFile.Url);
                if (downloadResponse.IsSuccessStatusCode)
                {
                    await using var fs = new FileStream(targetPath, FileMode.Create);
                    await downloadResponse.Content.CopyToAsync(fs);
                    
                    plugin.InstallText = "已安装 ✓";
                    plugin.CanInstall = false;
                    plugin.LatestVersion = latestVersion.VersionNumber ?? "";
                    
                    // 刷新本地插件列表和版本信息
                    LoadLocalPlugins();
                    ParseLocalPluginVersions();
                }
                else
                {
                    plugin.InstallText = "失败";
                    plugin.CanInstall = true;
                }
            }
            catch (TaskCanceledException)
            {
                plugin.InstallText = "超时";
                plugin.CanInstall = true;
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

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        var pluginsDir = Path.Combine(_server.ServerPath, "plugins");
        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
        Process.Start(new ProcessStartInfo { FileName = pluginsDir, UseShellExecute = true });
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

#region 本地插件模型

public class LocalPluginItem : INotifyPropertyChanged
{
    private string _fileName = "";
    private string _displayName = "";
    private string _fullPath = "";
    private bool _isEnabled;
    private Brush _statusBrush = new SolidColorBrush(Colors.Gray);
    private string _actionText = "";

    public string FileName { get => _fileName; set { _fileName = value; OnPropertyChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnPropertyChanged(); } }
    public string FullPath { get => _fullPath; set { _fullPath = value; OnPropertyChanged(); } }
    public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
    public Brush StatusBrush { get => _statusBrush; set { _statusBrush = value; OnPropertyChanged(); } }
    public string ActionText { get => _actionText; set { _actionText = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

#endregion

#region Modrinth模型

public class ModrinthPluginItem : INotifyPropertyChanged
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

// Modrinth API响应模型
public class ModrinthSearchResult
{
    [JsonPropertyName("hits")]
    public List<ModrinthHit>? Hits { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public class ModrinthHit
{
    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [JsonPropertyName("downloads")]
    public int Downloads { get; set; }

    [JsonPropertyName("versions")]
    public List<string>? Versions { get; set; }
    
    [JsonPropertyName("latest_version")]
    public string? LatestVersion { get; set; }
}

public class ModrinthVersion
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version_number")]
    public string? VersionNumber { get; set; }

    [JsonPropertyName("game_versions")]
    public List<string>? GameVersions { get; set; }

    [JsonPropertyName("files")]
    public List<ModrinthFile>? Files { get; set; }
}

public class ModrinthFile
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

#endregion
