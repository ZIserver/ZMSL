using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class BeginnerModePage : Page
{
    private readonly ServerDownloadService _downloadService;
    private readonly JavaManagerService _javaManager;
    private List<ServerDownloadService.ServerCoreCategory> _categories = new();
    private string? _selectedCore;
    private string? _selectedVersion;
    private string? _javaPath;
    private List<JavaInstallation> _availableJavas = new();
    private int _playerCapacity = 10;
    private bool _useLatestPurpur = true;
    private bool _isJavaDetectionRunning = false;

    public class JavaInstallation
    {
        public string Path { get; set; } = "";
        public int Version { get; set; }
        public string Source { get; set; } = "";
    }

    // 配置结果
    public LocalServer ConfiguredServer { get; private set; } = new();

    public BeginnerModePage()
    {
        this.InitializeComponent();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
        _javaManager = App.Services.GetRequiredService<JavaManagerService>();
        
        // 初始化配置对象
        ConfiguredServer.Mode = CreateMode.Beginner;
        ConfiguredServer.UseLatestPurpur = true;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // 设置ToggleSwitch初始状态
        UsePurpurToggle.IsOn = true;
        
        // 1. 启动后台 Java 检测 (渐进式更新)
        _ = StartJavaDetectionAsync();

        // 2. 优先加载服务端列表并显示
        try
        {
            await LoadServerCategories();
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载核心列表失败: {ex.Message}");
        }

        // 3. 如果使用 Purpur，初始化版本
        if (_useLatestPurpur)
        {
            _ = InitializePurpurVersionAsync();
        }
    }

    private async Task StartJavaDetectionAsync()
    {
        if (_isJavaDetectionRunning) return;
        _isJavaDetectionRunning = true;
        
        try
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                JavaInfoText.Text = "正在全盘扫描 Java 版本...";
                _availableJavas.Clear();
            });

            var progress = new Progress<ZMSL.App.Services.JavaInstallation>(java =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    // 避免重复添加
                    if (!_availableJavas.Any(j => j.Path == java.Path))
                    {
                        var newItem = new JavaInstallation
                        {
                            Path = java.Path,
                            Version = java.Version,
                            Source = java.Source
                        };
                        _availableJavas.Add(newItem);
                        
                        // 尝试实时匹配
                        if (!string.IsNullOrEmpty(_selectedVersion))
                        {
                            _ = DetectAndSetupJavaAsync(_selectedVersion);
                        }
                        else
                        {
                            JavaInfoText.Text = $"扫描中... 已找到 {_availableJavas.Count} 个 Java 版本";
                        }
                    }
                });
            });

            var installed = await _javaManager.DetectInstalledJavaAsync(progress);
            
            DispatcherQueue.TryEnqueue(async () =>
            {
                // 最终更新列表 (确保一致性)
                _availableJavas = installed.Select(j => new JavaInstallation
                {
                    Path = j.Path,
                    Version = j.Version,
                    Source = j.Source
                }).ToList();

                _isJavaDetectionRunning = false;

                if (!string.IsNullOrEmpty(_selectedVersion))
                {
                    await DetectAndSetupJavaAsync(_selectedVersion);
                }
                else
                {
                    if (_availableJavas.Count > 0)
                        JavaInfoText.Text = $"检测完成，共找到 {_availableJavas.Count} 个 Java 版本";
                    else
                        JavaInfoText.Text = "未检测到 Java，后续将自动下载";
                }
                
                UpdatePreview();
            });
        }
        catch (Exception ex)
        {
            _isJavaDetectionRunning = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                JavaInfoText.Text = $"检测出错: {ex.Message}";
            });
        }
    }

    private async Task InitializePurpurVersionAsync()
    {
        try 
        {
            var versions = await _downloadService.GetAvailableVersionsAsync("purpur");
            var latest = versions.FirstOrDefault();
            if (!string.IsNullOrEmpty(latest))
            {
                _selectedVersion = latest;
                DispatcherQueue.TryEnqueue(async () => 
                {
                    PreviewCoreText.Text = $"核心: Purpur ({latest})";
                    await DetectAndSetupJavaAsync(latest);
                });
            }
        }
        catch { /* 忽略获取版本失败，后续会重试 */ }
    }

    private async Task LoadServerCategories()
    {
        _categories = await _downloadService.GetServerCategoriesAsync();
        
        // 构造核心分类列表
        var categoryData = _categories.Select(cat => new 
        { 
            Name = cat.Name,
            DisplayName = cat.DisplayName
        }).ToList();
        
        CoreCategoriesPanel.ItemsSource = categoryData;
    }

    private async void UsePurpurToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _useLatestPurpur = UsePurpurToggle.IsOn;
        
        // 控制自定义核心选择的可见性
        CustomCoreGrid.Visibility = _useLatestPurpur ? Visibility.Collapsed : Visibility.Visible;
        VersionSelectionBorder.Visibility = _useLatestPurpur ? Visibility.Collapsed : Visibility.Visible;
        
        if (_useLatestPurpur)
        {
            _selectedCore = "purpur";
            try
            {
                var versions = await _downloadService.GetAvailableVersionsAsync("purpur");
                _selectedVersion = versions.FirstOrDefault();
            }
            catch
            {
                _selectedVersion = "latest";
            }
            
            PreviewCoreText.Text = $"核心: Purpur ({_selectedVersion ?? "latest"})";
            
            // 重新检测Java
            if (!string.IsNullOrEmpty(_selectedVersion))
            {
                await DetectAndSetupJavaAsync(_selectedVersion);
            }
        }
        else
        {
            _selectedCore = null;
            _selectedVersion = null;
            PreviewCoreText.Text = "核心: 未选择";
        }
        
        UpdatePreview();
        UpdateNextButtonState();
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string categoryName)
        {
            var category = _categories.FirstOrDefault(c => c.Name == categoryName);
            if (category != null)
            {
                SelectedCategoryText.Text = $"{category.DisplayName}";
                CoreListView.ItemsSource = category.Cores;
                _selectedCore = null;
                _selectedVersion = null;
                UpdatePreview();
                UpdateNextButtonState();
            }
        }
    }

    private async void Core_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string coreName)
        {
            _selectedCore = coreName;
            SelectedCoreDisplayText.Text = $"选择核心: {coreName}";
            VersionSelectionBorder.Visibility = Visibility.Visible;
            PreviewCoreText.Text = $"核心: {coreName}";
            
            try
            {
                var versions = await _downloadService.GetAvailableVersionsAsync(coreName);
                VersionListView.ItemsSource = versions;
                _selectedVersion = versions.FirstOrDefault();
                
                if (!string.IsNullOrEmpty(_selectedVersion))
                {
                    await DetectAndSetupJavaAsync(_selectedVersion);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"加载版本失败: {ex.Message}");
            }
            
            UpdatePreview();
            UpdateNextButtonState();
        }
    }

    private async void Version_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string version)
        {
            _selectedVersion = version;
            await DetectAndSetupJavaAsync(version);
            UpdatePreview();
            UpdateNextButtonState();
        }
    }

    private void PlayerCountRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlayerCountRadioButtons.SelectedItem is RadioButton selectedRadio)
        {
            if (int.TryParse(selectedRadio.Tag?.ToString(), out int capacity))
            {
                _playerCapacity = capacity;
                ConfiguredServer.PlayerCapacity = capacity;
                UpdatePreview();
            }
        }
    }

    private void UpdatePreview()
    {
        // 在InitializeComponent期间，某些控件可能还未初始化
        if (PreviewMemoryText == null || PreviewPlayersText == null || 
            PreviewJavaText == null || PreviewCoreText == null)
        {
            return;
        }
        
        // 更新核心预览
        if (_useLatestPurpur)
        {
            PreviewCoreText.Text = $"核心: Purpur ({_selectedVersion ?? "最新版本"})";
        }
        else if (!string.IsNullOrEmpty(_selectedCore) && !string.IsNullOrEmpty(_selectedVersion))
        {
            PreviewCoreText.Text = $"核心: {_selectedCore} {_selectedVersion}";
        }
        
        // 更新Java预览
        if (!string.IsNullOrEmpty(_javaPath))
        {
            var javaVersion = _availableJavas.FirstOrDefault(j => j.Path == _javaPath)?.Version ?? 0;
            PreviewJavaText.Text = javaVersion > 0 ? $"Java: Java {javaVersion}" : "Java: 已就绪";
        }
        else
        {
            PreviewJavaText.Text = "Java: 检测中...";
        }
        
        // 更新内存预览
        var memoryMB = CalculateMemoryForPlayers(_playerCapacity);
        PreviewMemoryText.Text = $"内存: {memoryMB / 1024}GB ({memoryMB}MB)";
        
        // 更新玩家预览
        var playerRange = GetPlayerRangeText(_playerCapacity);
        PreviewPlayersText.Text = $"预计玩家: {playerRange}";
        
        ConfiguredServer.MinMemoryMB = memoryMB;
        ConfiguredServer.MaxMemoryMB = memoryMB;
    }

    private int CalculateMemoryForPlayers(int players)
    {
        return players switch
        {
            <= 10 => 1024,   // 1GB
            <= 20 => 2048,   // 2GB
            <= 50 => 4096,   // 4GB
            _ => 8192        // 8GB
        };
    }

    private string GetPlayerRangeText(int players)
    {
        return players switch
        {
            <= 10 => "1-10人",
            <= 20 => "10-20人",
            <= 50 => "20-50人",
            _ => $"{players}人以上"
        };
    }

    private void UpdateNextButtonState()
    {
        if (NextButton == null || ServerNameTextBox == null || PlayerCountRadioButtons == null)
        {
            return;
        }
        
        bool canProceed = false;
        
        if (_useLatestPurpur)
        {
            // 使用Purpur最新版本时，只需要填写服务器名称和选择人数
            canProceed = !string.IsNullOrEmpty(ServerNameTextBox.Text.Trim()) &&
                        PlayerCountRadioButtons.SelectedIndex >= 0;
        }
        else
        {
            // 自定义核心时，需要完整选择
            canProceed = !string.IsNullOrEmpty(ServerNameTextBox.Text.Trim()) &&
                        PlayerCountRadioButtons.SelectedIndex >= 0 &&
                        !string.IsNullOrEmpty(_selectedCore) &&
                        !string.IsNullOrEmpty(_selectedVersion);
        }
        
        NextButton.IsEnabled = canProceed;
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInput())
        {
            return;
        }

        // 确保 Purpur 版本已获取 (防止 "latest" 导致下载失败)
        if (_useLatestPurpur && (string.IsNullOrEmpty(_selectedVersion) || _selectedVersion == "latest"))
        {
            NextButton.IsEnabled = false;
            try
            {
                var versions = await _downloadService.GetAvailableVersionsAsync("purpur");
                var latest = versions.FirstOrDefault();
                if (!string.IsNullOrEmpty(latest))
                {
                    _selectedVersion = latest;
                    DispatcherQueue.TryEnqueue(() => 
                    {
                        PreviewCoreText.Text = $"核心: Purpur ({latest})";
                    });
                    
                    // 重新检测Java需求
                    await DetectAndSetupJavaAsync(latest);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"获取核心版本失败: {ex.Message}");
            }
            finally
            {
                NextButton.IsEnabled = true;
            }
            
            if (string.IsNullOrEmpty(_selectedVersion) || _selectedVersion == "latest")
            {
                await ShowErrorDialog("无法获取核心版本，请检查网络连接。");
                return;
            }
        }

        // 设置配置
        ConfiguredServer.Name = ServerNameTextBox.Text.Trim();
        ConfiguredServer.PlayerCapacity = _playerCapacity;
        ConfiguredServer.UseLatestPurpur = _useLatestPurpur;
        ConfiguredServer.JavaPath = _javaPath; // 使用自动检测的Java路径
        
        if (!_useLatestPurpur)
        {
            ConfiguredServer.CoreType = _selectedCore!;
            ConfiguredServer.CoreVersion = _selectedVersion!;
            ConfiguredServer.MinecraftVersion = _selectedVersion!;
        }
        else
        {
            ConfiguredServer.CoreType = "purpur";
            ConfiguredServer.CoreVersion = _selectedVersion!; // 此时必定有值
            ConfiguredServer.MinecraftVersion = _selectedVersion!;
        }

        // 导航到确认页面
        Frame.Navigate(typeof(CreateConfirmationPage), ConfiguredServer);
    }

    /// <summary>
    /// 检测并设置Java
    /// </summary>
    private async Task DetectAndSetupJavaAsync(string mcVersion)
    {
        if (JavaInfoText == null) return;

        try
        {
            // 根据MC版本获取推荐的Java版本
            var recommendedVersion = JavaManagerService.GetRecommendedJavaVersion(mcVersion);
            
            // 查找已检测到的匹配版本
            var matching = _availableJavas.FirstOrDefault(j => j.Version == recommendedVersion);
            
            if (matching != null)
            {
                _javaPath = matching.Path;
                JavaInfoText.Text = $"已自动选择: Java {matching.Version}\n来源: {matching.Source}\n路径: {matching.Path}";
                System.Diagnostics.Debug.WriteLine($"[BeginnerMode] 已自动选择 Java {matching.Version}: {matching.Path}");
            }
            else
            {
                // 如果扫描仍在进行中，暂时不触发下载，只显示状态
                if (_isJavaDetectionRunning)
                {
                    JavaInfoText.Text = $"需要 Java {recommendedVersion}，正在全盘扫描中... (已找到 {_availableJavas.Count} 个版本)";
                    return;
                }

                JavaInfoText.Text = $"需要 Java {recommendedVersion}，正在下载...";
                
                // 获取或下载Java
                var javaPath = await _javaManager.GetOrDownloadJavaAsync(recommendedVersion, new Progress<double>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        JavaInfoText.Text = $"正在下载 Java {recommendedVersion}... {p:F1}%";
                    });
                }));
                
                if (!string.IsNullOrEmpty(javaPath))
                {
                    _javaPath = javaPath;
                    
                    // 添加到列表
                    _availableJavas.Add(new JavaInstallation
                    {
                        Path = javaPath,
                        Version = recommendedVersion,
                        Source = "Downloaded"
                    });
                    
                    JavaInfoText.Text = $"已自动选择: Java {recommendedVersion}\n来源: 已下载\n路径: {javaPath}";
                    System.Diagnostics.Debug.WriteLine($"[BeginnerMode] Java {recommendedVersion} 已下载并就绪: {javaPath}");
                }
                else
                {
                    JavaInfoText.Text = $"无法获取 Java {recommendedVersion}，请检查网络";
                }
            }
            
            UpdatePreview();
        }
        catch (Exception ex)
        {
            JavaInfoText.Text = $"Java检测失败: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[BeginnerMode] Java检测失败: {ex.Message}");
        }
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(ServerNameTextBox.Text))
        {
            ShowErrorDialog("请输入服务器名称").ConfigureAwait(false);
            return false;
        }

        if (PlayerCountRadioButtons.SelectedIndex < 0)
        {
            ShowErrorDialog("请选择预计玩家人数").ConfigureAwait(false);
            return false;
        }

        if (!_useLatestPurpur)
        {
            if (string.IsNullOrEmpty(_selectedCore))
            {
                ShowErrorDialog("请选择服务端核心").ConfigureAwait(false);
                return false;
            }

            if (string.IsNullOrEmpty(_selectedVersion))
            {
                ShowErrorDialog("请选择版本").ConfigureAwait(false);
                return false;
            }
        }

        return true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(CreateModeSelectionPage));
    }

    private async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "配置错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}