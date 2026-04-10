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

public sealed partial class ModsManagerPage : Page
{
    // HttpClient 延迟初始化
    private static HttpClient? _apiClient;
    private static HttpClient? _downloadClient;
    
    // Modrinth API
    private static HttpClient ApiClient => _apiClient ??= CreateApiClient();
    private static HttpClient DownloadClient => _downloadClient ??= CreateDownloadClient();
    
    private static HttpClient CreateApiClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.modrinth.com/v2/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/1.0 (zmsl@example.com)");
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
    private bool _isInstalling = false;
    private readonly ObservableCollection<LocalModItem> _allLocalMods = new();
    private readonly ObservableCollection<LocalModItem> _filteredLocalMods = new();
    private readonly ObservableCollection<ModrinthModItem> _onlineMods = new();
    
    // 分页相关
    private int _currentPage = 0;
    private int _totalHits = 0;
    private const int PageSize = 20;
    private string? _currentKeyword;
    
    private readonly Dictionary<string, string> _localModVersions = new(StringComparer.OrdinalIgnoreCase);

    public ModsManagerPage()
    {
        this.InitializeComponent();
        LocalModList.ItemsSource = _filteredLocalMods;
        OnlineModList.ItemsSource = _onlineMods;
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
        LoadLocalMods();
        ParseLocalModVersions();
        _currentPage = 0;
        _currentKeyword = null;
        await LoadOnlineModsAsync();
    }
    
    private void ParseLocalModVersions()
    {
        _localModVersions.Clear();
        foreach (var mod in _allLocalMods)
        {
            var fileName = mod.FileName.Replace(".disabled", "");
            _localModVersions[fileName] = "installed";
        }
    }

    #region 本地模组

    private void LoadLocalMods()
    {
        if (_server == null) return;

        _allLocalMods.Clear();

        var modsDir = Path.Combine(_server.ServerPath, "mods");
        if (!Directory.Exists(modsDir))
        {
            Directory.CreateDirectory(modsDir);
            ApplyLocalFilter();
            return;
        }

        foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;

            _allLocalMods.Add(new LocalModItem
            {
                FileName = fileName,
                DisplayName = Path.GetFileNameWithoutExtension(file),
                FullPath = file,
                IsEnabled = true,
                StatusBrush = new SolidColorBrush(Colors.LimeGreen),
                ActionText = "禁用"
            });
        }

        foreach (var file in Directory.GetFiles(modsDir, "*.jar.disabled"))
        {
            var fileName = Path.GetFileName(file);
            var originalName = fileName.Replace(".disabled", "");

            _allLocalMods.Add(new LocalModItem
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
        _filteredLocalMods.Clear();

        foreach (var p in _allLocalMods.OrderBy(x => x.DisplayName))
        {
            bool show = filterIndex switch
            {
                1 => p.IsEnabled,
                2 => !p.IsEnabled,
                _ => true
            };
            if (show) _filteredLocalMods.Add(p);
        }

        InstalledCountText.Text = $"({_allLocalMods.Count})";
        LocalEmptyPanel.Visibility = _filteredLocalMods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LocalFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LocalEmptyPanel == null || InstalledCountText == null) return;
        ApplyLocalFilter();
    }

    private async void ToggleMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LocalModItem mod)
        {
            try
            {
                if (mod.IsEnabled)
                {
                    var newPath = mod.FullPath + ".disabled";
                    File.Move(mod.FullPath, newPath);
                    mod.FullPath = newPath;
                    mod.FileName = Path.GetFileName(newPath);
                    mod.IsEnabled = false;
                    mod.StatusBrush = new SolidColorBrush(Colors.Orange);
                    mod.ActionText = "启用";
                }
                else
                {
                    var newPath = mod.FullPath.Replace(".disabled", "");
                    File.Move(mod.FullPath, newPath);
                    mod.FullPath = newPath;
                    mod.FileName = Path.GetFileName(newPath);
                    mod.IsEnabled = true;
                    mod.StatusBrush = new SolidColorBrush(Colors.LimeGreen);
                    mod.ActionText = "禁用";
                }
                ApplyLocalFilter();
            }
            catch (Exception ex)
            {
                await ShowDialogAsync("错误", ex.Message);
            }
        }
    }

    private async void DeleteMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LocalModItem mod)
        {
            var confirm = new ContentDialog
            {
                Title = "确认删除",
                Content = $"确定要删除模组 \"{mod.DisplayName}\" 吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            if (await confirm.ShowAsync() == ContentDialogResult.Primary)
            {
                try
                {
                    File.Delete(mod.FullPath);
                    _allLocalMods.Remove(mod);
                    _filteredLocalMods.Remove(mod);
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

    #region Modrinth 在线模组

    private async Task LoadOnlineModsAsync(string? keyword = null, bool resetPage = true)
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
            
            _onlineMods.Clear();

            var mcVersion = _server.MinecraftVersion;
            var modLoader = GetModLoaderType(_server.CoreType);

            // Modrinth Search API
            var offset = _currentPage * PageSize;
            var query = string.IsNullOrWhiteSpace(_currentKeyword) ? "" : Uri.EscapeDataString(_currentKeyword);
            
            // 构建 facets
            var facetsList = new List<string>();
            facetsList.Add("[\"project_type:mod\"]"); // 只搜索模组
            
            if (!string.IsNullOrEmpty(mcVersion))
            {
                facetsList.Add($"[\"versions:{mcVersion}\"]");
            }
            
            if (!string.IsNullOrEmpty(modLoader))
            {
                facetsList.Add($"[\"categories:{modLoader}\"]");
            }
            
            var facets = "[" + string.Join(",", facetsList) + "]";
            var facetsParam = Uri.EscapeDataString(facets);
            
            var url = $"search?query={query}&facets={facetsParam}&offset={offset}&limit={PageSize}&index=relevance";
            
            HttpResponseMessage response;
            try 
            {
                response = await ApiClient.GetAsync(url);
            }
            catch (Exception)
            {
                OnlineLoadingRing.IsActive = false;
                OnlineEmptyPanel.Visibility = Visibility.Visible;
                return;
            }

            OnlineLoadingRing.IsActive = false;

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<ModrinthModSearchResult>(json);

                if (result?.Hits != null)
                {
                    _totalHits = result.TotalHits;
                    
                    foreach (var mod in result.Hits)
                    {
                        string installText = "安装";
                        bool canInstall = true;
                        
                        // 简单检查是否已安装（根据slug）
                        if (_localModVersions.ContainsKey(mod.Slug ?? "") || 
                            _localModVersions.ContainsKey(mod.Title ?? ""))
                        {
                            installText = "已安装";
                            canInstall = false; 
                        }
                        
                        _onlineMods.Add(new ModrinthModItem
                        {
                            ModId = mod.ProjectId ?? "", // Modrinth uses string ID
                            Slug = mod.Slug,
                            Name = mod.Title ?? "未知",
                            Author = mod.Author ?? "未知",
                            Summary = mod.Description ?? "",
                            IconUrl = !string.IsNullOrEmpty(mod.IconUrl) ? mod.IconUrl : "ms-appx:///Assets/app-icon.png",
                            Downloads = mod.Downloads,
                            InstallText = installText,
                            CanInstall = canInstall
                        });
                    }
                }

                UpdatePaginationUI();
                OnlineEmptyPanel.Visibility = _onlineMods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                Debug.WriteLine($"Modrinth API Error: {response.StatusCode}");
                OnlineEmptyPanel.Visibility = Visibility.Visible;
                await ShowDialogAsync("API 错误", $"无法连接到 Modrinth API ({response.StatusCode})");
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
        var totalPages = Math.Max(1, (_totalHits + PageSize - 1) / PageSize);
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
            await LoadOnlineModsAsync(_currentKeyword, resetPage: false);
        }
    }
    
    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        if ((_currentPage + 1) * PageSize < _totalHits)
        {
            _currentPage++;
            await LoadOnlineModsAsync(_currentKeyword, resetPage: false);
        }
    }

    private static string? GetModLoaderType(string coreType)
    {
        return coreType?.ToLower() switch
        {
            "forge" or "mohist" or "arclight" or "arclight-forge" or "arclight-neoforge" or "youer" or "magma" or "catserver" or "spongeforge" or "neoforge" => "forge",
            "fabric" or "arclight-fabric" or "banner" => "fabric",
            "quilt" => "quilt",
            _ => null // Any / Unknown
        };
    }

    private void OnlineSearch_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = LoadOnlineModsAsync(OnlineSearchBox.Text);
        }
    }

    private async void InstallMod_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is not Button btn || btn.DataContext is not ModrinthModItem mod) return;
        if (!mod.CanInstall || _isInstalling) return;

        _isInstalling = true;
        
        try
        {
            btn.IsEnabled = false;
            mod.InstallText = "获取版本...";
            mod.CanInstall = false;

            var mcVersion = _server.MinecraftVersion;
            var modLoader = GetModLoaderType(_server.CoreType);

            // 获取版本列表
            // /project/{id|slug}/version
            var versionsUrl = $"project/{mod.Slug}/version";
            
            // 添加过滤参数
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(modLoader))
            {
                queryParams.Add($"loaders=[\"{modLoader}\"]");
            }
            if (!string.IsNullOrEmpty(mcVersion))
            {
                queryParams.Add($"game_versions=[\"{mcVersion}\"]");
            }
            
            if (queryParams.Count > 0)
            {
                versionsUrl += "?" + string.Join("&", queryParams);
            }
            
            HttpResponseMessage response;
            try
            {
                response = await ApiClient.GetAsync(versionsUrl);
            }
            catch
            {
                mod.InstallText = "失败";
                mod.CanInstall = true;
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                mod.InstallText = "无版本";
                mod.CanInstall = true;
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var versions = JsonSerializer.Deserialize<List<ModrinthModVersion>>(json);

            if (versions == null || versions.Count == 0)
            {
                mod.InstallText = "无兼容版本";
                mod.CanInstall = true;
                return;
            }

            // 找到最新的版本（API默认按发布时间排序，第一个即最新）
            var latestVersion = versions.FirstOrDefault();
            
            // 找到主文件
            var primaryFile = latestVersion?.Files?.FirstOrDefault(f => f.Primary) 
                              ?? latestVersion?.Files?.FirstOrDefault();

            if (primaryFile?.Url == null)
            {
                mod.InstallText = "无法下载";
                mod.CanInstall = true;
                return;
            }

            mod.InstallText = "下载中...";

            // 下载文件
            var modsDir = Path.Combine(_server.ServerPath, "mods");
            if (!Directory.Exists(modsDir))
                Directory.CreateDirectory(modsDir);

            var fileName = primaryFile.Filename ?? $"{mod.Slug}-{latestVersion?.VersionNumber}.jar";
            var targetPath = Path.Combine(modsDir, fileName);

            try
            {
                using var downloadResponse = await DownloadClient.GetAsync(primaryFile.Url);
                if (downloadResponse.IsSuccessStatusCode)
                {
                    await using var fs = new FileStream(targetPath, FileMode.Create);
                    await downloadResponse.Content.CopyToAsync(fs);
                    
                    mod.InstallText = "已安装 ✓";
                    mod.CanInstall = false;
                    
                    // 刷新本地模组列表
                    LoadLocalMods();
                    ParseLocalModVersions();
                }
                else
                {
                    mod.InstallText = "下载失败";
                    mod.CanInstall = true;
                }
            }
            catch
            {
                mod.InstallText = "出错";
                mod.CanInstall = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Install error: {ex.Message}");
            mod.InstallText = "失败";
            mod.CanInstall = true;
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
        var modsDir = Path.Combine(_server.ServerPath, "mods");
        if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
        Process.Start(new ProcessStartInfo { FileName = modsDir, UseShellExecute = true });
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

#region 本地模组模型

public class LocalModItem : INotifyPropertyChanged
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

public class ModrinthModItem : INotifyPropertyChanged
{
    private string _modId = "";
    private string? _slug = "";
    private string _name = "";
    private string _author = "";
    private string _summary = "";
    private string _iconUrl = "";
    private int _downloads;
    private string _installText = "安装";
    private bool _canInstall = true;

    public string ModId { get => _modId; set { _modId = value; OnPropertyChanged(); } }
    public string? Slug { get => _slug; set { _slug = value; OnPropertyChanged(); } }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Author { get => _author; set { _author = value; OnPropertyChanged(); } }
    public string Summary { get => _summary; set { _summary = value; OnPropertyChanged(); } }
    public string IconUrl { get => _iconUrl; set { _iconUrl = value; OnPropertyChanged(); } }
    public int Downloads { get => _downloads; set { _downloads = value; OnPropertyChanged(); } }
    public string DownloadsText => Downloads >= 1000000 ? $"{Downloads / 1000000.0:F1}M" : Downloads >= 1000 ? $"{Downloads / 1000.0:F1}K" : Downloads.ToString();
    public string InstallText { get => _installText; set { _installText = value; OnPropertyChanged(); } }
    public bool CanInstall { get => _canInstall; set { _canInstall = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ModrinthModSearchResult
{
    [JsonPropertyName("hits")]
    public List<ModrinthMod>? Hits { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("total_hits")]
    public int TotalHits { get; set; }
}

public class ModrinthMod
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
}

public class ModrinthModVersion
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; set; }
    
    [JsonPropertyName("version_number")]
    public string? VersionNumber { get; set; }

    [JsonPropertyName("files")]
    public List<ModrinthModFile>? Files { get; set; }
}

public class ModrinthModFile
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

#endregion
