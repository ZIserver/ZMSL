using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using ZMSL.App.Services;
using ZMSL.App.Views;

namespace ZMSL.App;

// P/Invoke 声明
internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern bool ReleaseCapture();
    
    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    
    internal const uint WM_NCLBUTTONDOWN = 0x00A1;
    internal const uint HTCAPTION = 2;

    // Window Subclassing for MinSize
    internal const int GWLP_WNDPROC = -4;
    internal const int WM_GETMINMAXINFO = 0x0024;
    internal const int WM_SIZE = 0x0005;
    internal const int SIZE_MINIMIZED = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    internal static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll")]
    internal static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool AppendMenu(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    internal static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    internal static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const uint IMAGE_ICON = 1;
    internal const uint LR_LOADFROMFILE = 0x0010;
    internal const uint WM_USER = 0x0400;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_LBUTTONDBLCLK = 0x0203;
    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint NIF_MESSAGE = 0x00000001;
    internal const uint NIF_ICON = 0x00000002;
    internal const uint NIF_TIP = 0x00000004;
    internal const uint NIF_INFO = 0x00000010;
    internal const uint NIIF_INFO = 0x00000001;
    internal const uint TPM_LEFTALIGN = 0x0000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint MF_STRING = 0x0000;
    internal const uint MF_SEPARATOR = 0x0800;
    internal const uint WM_TRAYICON = WM_USER + 1;
    internal const uint TRAY_CMD_SHOW = 1001;
    internal const uint TRAY_CMD_EXIT = 1002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}

public sealed partial class MainWindow : Window
{
    private readonly AuthService _authService;
    private readonly ServerManagerService _serverManager;
    private readonly FrpService _frpService;
    private readonly PlayerForumService _forumService;
    private readonly FrpcDownloadService _frpcDownloadService;
    private readonly object _trafficLock = new();
    private long _lastTotalBytesSent;
    private long _lastTotalBytesReceived;
    private double _uploadSpeedBytes;
    private double _downloadSpeedBytes;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private NativeMethods.NOTIFYICONDATA _trayIconData;
    private IntPtr _trayIconHandle = IntPtr.Zero;
    private bool _trayIconRegistered = false;
    private bool _isClosingConfirmed = false;
    private bool _exitFromTrayRequested = false;
    private bool _ignoreNextMinimizeToTray = false;
    private bool _hasShownTrayBalloon = false;
    private bool _isDarkTheme = false;
    
    // WndProc Hook
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _newWndProc;
    
    public Microsoft.UI.Xaml.Controls.Frame ContentFramePublic => ContentFrame;

    public MainWindow()
    {
        this.InitializeComponent();
        
        _authService = App.Services.GetRequiredService<AuthService>();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _frpService = App.Services.GetRequiredService<FrpService>();
        _forumService = App.Services.GetRequiredService<PlayerForumService>();
        _frpcDownloadService = App.Services.GetRequiredService<FrpcDownloadService>();
        
        // 设置窗口
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        
        // 设置窗口大小
        _appWindow.Resize(new Windows.Graphics.SizeInt32(1114, 900));

        // Hook WndProc for MinSize
        _newWndProc = new WndProcDelegate(NewWndProc);
        _oldWndProc = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
        
        // 设置窗口属性
        var presenter = _appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.IsMinimizable = true;
        }
        
        // 设置窗口图标和标题
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }
        }
        catch { }
        
        _appWindow.Title = "智穗MC开服器";
        
        // 使用自定义标题栏(无边框窗口)
        ExtendsContentIntoTitleBar = true;
        
        // 给拖动区域添加事件 - 必须在 InitializeComponent 之后
        if (DragRegion != null)
        {
            DragRegion.PointerPressed += DragRegion_PointerPressed;
        }
        
        // 监听窗口关闭事件
        _appWindow.Closing += AppWindow_Closing;
        this.Closed += MainWindow_Closed;
        InitializeTrayIcon();

        // 设置背景效果
        // this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        // 初始化时不直接设置，而是通过 UpdateSystemBackdrop 方法根据配置设置
        // 但此时配置可能还没加载，先默认开启，后续由 App.xaml.cs 或 SettingsViewModel 触发更新
        // 为了避免黑屏，初始状态设为默认背景，等待 App.xaml.cs 调用 UpdateSystemBackdrop(true) 时再设为透明
        
        // 预设 SystemBackdrop 实例，但不应用透明背景，直到明确启用
        // 注意：如果在 XAML 中已经设置了背景颜色，SystemBackdrop 会被遮挡
        /*
        if (Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
        {
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        }
        else if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        */
        
        // 初始化主题 - 优先使用系统设置
        InitializeThemeFromSystem();
        UpdateThemeUI();
        ApplyTheme();

        // 订阅登录状态变化
        _authService.LoginStateChanged += OnLoginStateChanged;
        _serverManager.ServerStatusChanged += ServerManager_ServerStatusChanged;
        
        // 检查登录状态
        // CheckLoginAndNavigate();
        ContentFrame.Navigate(typeof(HomePage));
        
        // 更新用户信息显示
        UpdateUserInfo();
        
        // 标题栏显示当前版本（与 ZMSL.App.csproj 中的 Version 一致）
        // VersionText.Text = "v" + App.UpdateService.CurrentVersion;
    }

    public void MinimizeToTray()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ignoreNextMinimizeToTray = true;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        if (!_hasShownTrayBalloon)
        {
            ShowTrayBalloon("智穗 MC 开服器", "应用已最小化到系统托盘。");
            _hasShownTrayBalloon = true;
        }
    }

    public void RestoreFromTray()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _ignoreNextMinimizeToTray = true;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(hwnd);
        Activate();
    }

    private string FormatBytesPerSecond(double bytesPerSecond)
    {
        var units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        var value = bytesPerSecond < 0 ? 0 : bytesPerSecond;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.0} {units[unitIndex]}";
    }

    private void RefreshNetworkTraffic()
    {
        try
        {
            long totalBytesSent = 0;
            long totalBytesReceived = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                var statistics = nic.GetIPStatistics();
                totalBytesSent += statistics.BytesSent;
                totalBytesReceived += statistics.BytesReceived;
            }

            lock (_trafficLock)
            {
                if (_lastTotalBytesSent != 0 || _lastTotalBytesReceived != 0)
                {
                    _uploadSpeedBytes = Math.Max(0, totalBytesSent - _lastTotalBytesSent);
                    _downloadSpeedBytes = Math.Max(0, totalBytesReceived - _lastTotalBytesReceived);
                }

                _lastTotalBytesSent = totalBytesSent;
                _lastTotalBytesReceived = totalBytesReceived;
            }
        }
        catch
        {
        }
    }

    private async Task<string[]> GetRunningServerNamesAsync()
    {
        try
        {
            var runningIds = _serverManager.GetRunningServerIds();
            if (runningIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var servers = await _serverManager.GetServersAsync();
            return servers.Where(s => runningIds.Contains(s.Id)).Select(s => s.Name).OrderBy(n => n).ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private void ServerManager_ServerStatusChanged(object? sender, ServerStatusEventArgs e)
    {
        // 服务器状态变化时的处理
    }

    private void InitializeTrayIcon()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            _trayIconHandle = NativeMethods.LoadImage(IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON, 16, 16, NativeMethods.LR_LOADFROMFILE);
        }

        _trayIconData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_TIP,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon = _trayIconHandle,
            szTip = "智穗MC开服器",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            guidItem = Guid.Empty
        };

        if (_trayIconHandle != IntPtr.Zero)
        {
            _trayIconData.uFlags |= NativeMethods.NIF_ICON;
        }

        _trayIconRegistered = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _trayIconData);
    }

    private void ShowTrayBalloon(string title, string message)
    {
        if (!_trayIconRegistered)
        {
            return;
        }

        _trayIconData.uFlags = NativeMethods.NIF_INFO | NativeMethods.NIF_MESSAGE | NativeMethods.NIF_TIP;
        if (_trayIconHandle != IntPtr.Zero)
        {
            _trayIconData.uFlags |= NativeMethods.NIF_ICON;
        }

        _trayIconData.szInfoTitle = title;
        _trayIconData.szInfo = message;
        _trayIconData.dwInfoFlags = NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _trayIconData);
        _trayIconData.szInfo = string.Empty;
        _trayIconData.szInfoTitle = string.Empty;
    }

    private void ShowTrayContextMenu()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (!NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, NativeMethods.TRAY_CMD_SHOW, "显示主窗口");
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, string.Empty);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, NativeMethods.TRAY_CMD_EXIT, "退出应用");
            NativeMethods.SetMenuDefaultItem(menu, NativeMethods.TRAY_CMD_SHOW, 0);
            NativeMethods.PostMessage(hwnd, 0, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.TrackPopupMenu(menu, NativeMethods.TPM_LEFTALIGN | NativeMethods.TPM_RIGHTBUTTON, point.x, point.y, 0, hwnd, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void RemoveTrayIcon()
    {
        if (_trayIconRegistered)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _trayIconData);
            _trayIconRegistered = false;
        }

        if (_trayIconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }
    }

    private void DragRegion_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // 只处理左键点击
        if (e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
        {
            // 开始拖动窗口
            var presenter = _appWindow?.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter?.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
            {
                presenter.Restore();
            }
            
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
        }
    }

    private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_isClosingConfirmed) return;
        if (!_exitFromTrayRequested)
        {
            args.Cancel = true;
            MinimizeToTray();
            return;
        }

        var runningServers = _serverManager.GetRunningServerIds();
        var frpConnected = _frpService.IsConnected;
        var meFrpService = App.Services.GetRequiredService<MeFrpService>();
        var starryFrpService = App.Services.GetRequiredService<StarryFrpService>();
        var meFrpRunning = meFrpService.CurrentRunningProxyId.HasValue;
        var starryFrpRunning = starryFrpService.CurrentRunningProxyId.HasValue;

        if (runningServers.Count > 0 || frpConnected || meFrpRunning || starryFrpRunning)
        {
            args.Cancel = true;

            var message = "以下服务将在关闭后停止:\n";
            if (runningServers.Count > 0)
                message += $"- {runningServers.Count} 个运行中的服务器\n";
            if (frpConnected)
                message += "- FRP 内网穿透连接\n";
            if (meFrpRunning)
                message += $"- MeFrp 隧道：{meFrpService.CurrentRunningProxyName}\n";
            if (starryFrpRunning)
                message += $"- StarryFrp 隧道：{starryFrpService.CurrentRunningProxyName}\n";
            message += "\n确定要关闭吗？";

            var dialog = new ContentDialog
            {
                Title = "确认关闭",
                Content = message,
                PrimaryButtonText = "确定关闭",
                CloseButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await _serverManager.StopAllServersAsync();
                if (frpConnected)
                    await _frpService.StopTunnelAsync();
                if (meFrpRunning)
                    await meFrpService.StopTunnelAsync();
                if (starryFrpRunning)
                    await starryFrpService.StopTunnelAsync();

                _isClosingConfirmed = true;
                RemoveTrayIcon();
                _appWindow?.Destroy();
            }
        }
    }

    private IntPtr NewWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_TRAYICON)
        {
            switch ((uint)lParam)
            {
                case NativeMethods.WM_LBUTTONDBLCLK:
                    RestoreFromTray();
                    return IntPtr.Zero;
                case NativeMethods.WM_RBUTTONUP:
                    ShowTrayContextMenu();
                    return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_COMMAND)
        {
            switch ((uint)wParam.ToInt64() & 0xFFFF)
            {
                case NativeMethods.TRAY_CMD_SHOW:
                    RestoreFromTray();
                    return IntPtr.Zero;
                case NativeMethods.TRAY_CMD_EXIT:
                    _exitFromTrayRequested = true;
                    RestoreFromTray();
                    _appWindow?.Destroy();
                    return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_SIZE && wParam == (IntPtr)NativeMethods.SIZE_MINIMIZED)
        {
            if (_ignoreNextMinimizeToTray)
            {
                _ignoreNextMinimizeToTray = false;
            }
            else
            {
                MinimizeToTray();
                return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            var dpi = NativeMethods.GetDpiForWindow(hWnd);
            float scalingFactor = (float)dpi / 96;
            
            var minMaxInfo = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            minMaxInfo.ptMinTrackSize.x = (int)(1114 * scalingFactor);
            minMaxInfo.ptMinTrackSize.y = (int)(900 * scalingFactor);
            Marshal.StructureToPtr(minMaxInfo, lParam, true);
        }
        return NativeMethods.CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        try
        {
            _serverManager.ServerStatusChanged -= ServerManager_ServerStatusChanged;
            RemoveTrayIcon();
        }
        catch
        {
        }

        try
        {
            var meFrpService = App.Services.GetRequiredService<MeFrpService>();
            await meFrpService.StopTunnelAsync();
        }
        catch
        {
        }

        try
        {
            var starryFrpService = App.Services.GetRequiredService<StarryFrpService>();
            await starryFrpService.StopTunnelAsync();
        }
        catch
        {
        }

        try
        {
            ChildProcessManager.Instance.TerminateAll();
        }
        catch
        {
        }
    }

    private void CheckLoginAndNavigate()
    {
        // 简化登录检查逻辑
        if (_authService.IsLoggedIn)
        {
            ContentFrame.Navigate(typeof(HomePage));
            NavView.SelectedItem = NavView.MenuItems[0];
        }
        else
        {
            ContentFrame.Navigate(typeof(LoginPage));
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            
            Type? pageType = tag switch
            {
                "Home" => typeof(HomePage),
                "ServerCore" => typeof(ServerCorePage),
                "MyServer" => typeof(MyServerPage),
                "GroupServer" => typeof(GroupServerListPage),
                "LogAnalysis" => typeof(LogAnalysisPage),
                "LinuxNode" => typeof(LinuxNodePage),
                "NodeSelection" => typeof(NodeSelectionPage),
                "LocalJava" => typeof(LocalJavaManagePage),
                "Forum" => typeof(ForumPage),
                "Lottery" => typeof(LotteryPage),
                "Frp" => typeof(FrpPage),
                "MeFrp" => typeof(MeFrpPage),
                "MSLFrp" => typeof(DocumentationPage),
                "SakuraFrp" => typeof(DocumentationPage),
                "StarryFrp" => typeof(StarryFrpPage),
                "Documentation" => typeof(DocumentationPage),
                "Settings" => typeof(SettingsPage),
                _ => null
            };

            if (pageType != null)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }

    private void UserNavItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // 阻止导航行为
        e.Handled = true;
        
        if (_authService.IsLoggedIn)
        {
            ContentFrame.Navigate(typeof(UserProfilePage));
        }
        else
        {
            ContentFrame.Navigate(typeof(LoginPage));
        }
    }

    private void ThemeToggleNavItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        // 阻止导航行为
        e.Handled = true;
        
        _isDarkTheme = !_isDarkTheme;
        ApplyTheme();
        UpdateThemeUI();
        
        // 保存用户的主题偏好到数据库
        SaveThemePreferenceAsync();
    }

    /// <summary>
    /// 保存主题偏好到数据库
    /// </summary>
    private async void SaveThemePreferenceAsync()
    {
        try
        {
            var db = App.Services.GetRequiredService<DatabaseService>();
            var settings = await db.GetSettingsAsync();
            settings.Theme = _isDarkTheme ? "Dark" : "Light";
            await db.SaveSettingsAsync(settings);
            System.Diagnostics.Debug.WriteLine($"[Theme] 主题偏好已保存: {settings.Theme}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Theme] 保存主题偏好失败: {ex.Message}");
        }
    }

    private void OnLoginStateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // 登录状态变化处理
            if (_authService.IsLoggedIn && ContentFrame.Content is LoginPage)
            {
                ContentFrame.Navigate(typeof(HomePage));
                NavView.SelectedItem = NavView.MenuItems[0];
            }
            
            // 更新用户信息显示
            UpdateUserInfo();
        });
    }

    private void UpdateUserInfo()
    {
        if (_authService.IsLoggedIn && _authService.CurrentUser != null)
        {
            UsernameText.Text = _authService.CurrentUser.Username;
            TitleBarUserArea.Visibility = Visibility.Visible;
            
            // 更新头像
            SetAvatar(UserAvatar, _authService.CurrentUser.AvatarUrl);
        }
        else
        {
            UsernameText.Text = "未登录";
            TitleBarUserArea.Visibility = Visibility.Collapsed;
            UserAvatar.ProfilePicture = null;
        }
    }

    private void SetAvatar(PersonPicture personPicture, string? avatarUrl)
    {
        if (!string.IsNullOrEmpty(avatarUrl))
        {
            var url = avatarUrl;
            if (!url.StartsWith("http"))
            {
                 if (!url.StartsWith("/")) url = "/" + url;
                 url = "https://msl.v2.zhsdev.top" + url;
            }
            try
            {
                personPicture.ProfilePicture = new BitmapImage(new Uri(url));
            }
            catch
            {
                personPicture.ProfilePicture = null;
            }
        }
        else
        {
            personPicture.ProfilePicture = null;
        }
    }

    private void ApplyTheme()
    {
        if (Content is FrameworkElement rootElement)
        {
            var theme = _isDarkTheme ? ElementTheme.Dark : ElementTheme.Light;
            rootElement.RequestedTheme = theme;
            
            // 如果未启用 SystemBackdrop (即处于普通背景模式)，需要手动更新背景色
            // 因为 ThemeResource 有时不会自动刷新，或者被之前的透明色覆盖
            if (this.SystemBackdrop == null)
            {
                NavView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
                AppTitleBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
            }
        }
    }

    private void UpdateThemeUI()
    {
        // 更新主题图标
    }

    /// <summary>
    /// 从系统设置初始化主题
    /// </summary>
    private void InitializeThemeFromSystem()
    {
        try
        {
            // 获取系统的亮暗色设置
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var systemTheme = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
            
            // 如果背景色接近白色，则为亮色模式；接近黑色，则为暗色模式
            // RGB 值的平均值 > 128 表示亮色，否则为暗色
            var brightness = (systemTheme.R + systemTheme.G + systemTheme.B) / 3;
            _isDarkTheme = brightness < 128;
            
            System.Diagnostics.Debug.WriteLine($"[Theme] 系统主题: {(_isDarkTheme ? "暗色" : "亮色")} (亮度: {brightness})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Theme] 获取系统主题失败: {ex.Message}，使用默认亮色主题");
            _isDarkTheme = false;
        }
        
        // 订阅系统主题变化事件
        SubscribeToSystemThemeChanges();
    }

    /// <summary>
    /// 订阅系统主题变化事件
    /// </summary>
    private void SubscribeToSystemThemeChanges()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            uiSettings.ColorValuesChanged += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    // 系统主题变化时，重新初始化主题
                    InitializeThemeFromSystem();
                    ApplyTheme();
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Theme] 订阅系统主题变化失败: {ex.Message}");
        }
    }

    public void UpdateSystemBackdrop(bool enable, int intensityLevel = 0)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (enable)
            {
                // 设置背景为透明，以便显示 SystemBackdrop
                AppTitleBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                NavView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                // StatusBarGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

                if (intensityLevel >= 2 && Microsoft.UI.Composition.SystemBackdrops.DesktopAcrylicController.IsSupported())
                {
                    if (this.SystemBackdrop is not Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop)
                    {
                        this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                    }
                }
                else if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
                {
                    var micaBackdrop = this.SystemBackdrop as Microsoft.UI.Xaml.Media.MicaBackdrop;
                    if (micaBackdrop == null)
                    {
                        micaBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                        this.SystemBackdrop = micaBackdrop;
                    }
                    
                    // 根据强度设置 MicaKind
                    // 0 = Base (较柔和/标准)
                    // 1 = BaseAlt (颜色稍深/变化)
                    var targetKind = (intensityLevel == 1) ? Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt : Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base;
                    if (micaBackdrop.Kind != targetKind)
                    {
                        micaBackdrop.Kind = targetKind;
                    }
                }
            }
            else
            {
                this.SystemBackdrop = null;
                
                // 恢复默认背景颜色
                // 注意：在代码中直接通过Application.Current.Resources获取ThemeResource可能失败
                // 因此这里使用固定的默认主题资源画刷，或者让XAML处理
                // 更可靠的方式是将背景设为null（如果父容器有背景），或者设为特定的SolidColorBrush
                // WinUI 3 NavigationView 默认背景通常是 ApplicationPageBackgroundThemeBrush
                
                if (Application.Current.Resources.TryGetValue("SystemControlBackgroundChromeMediumBrush", out var brush1) && brush1 is Microsoft.UI.Xaml.Media.Brush b1)
                    AppTitleBar.Background = b1;
                else
                    AppTitleBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    
                if (Content is FrameworkElement root && root.Resources.TryGetValue("WindowBackground", out var windowBg) && windowBg is Microsoft.UI.Xaml.Media.Brush winBrush)
                {
                    // 强制使用软件主题颜色，而不是跟随系统
                    // WindowBackground 绑定了 ThemeResource，它会自动跟随 ElementTheme (即软件主题)
                    // 但我们需要确保 RootElement 的 RequestedTheme 已经正确应用
                    NavView.Background = winBrush;
                }
                else
                {
                    // 如果资源获取失败，手动根据软件主题设置颜色
                    NavView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
                }

                // 强制刷新主题，确保颜色资源正确应用
                // 但要先确保 SystemBackdrop 为 null，否则 ApplyTheme 可能误判
                this.SystemBackdrop = null;
                ApplyTheme();

                if (_isDarkTheme)
                {
                     NavView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
                     AppTitleBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 32, 32, 32));
                }
                else
                {
                     NavView.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.WhiteSmoke);
                     // StatusBarGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray);
                     AppTitleBar.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
            }
        });
    }

    public void UpdateCustomBackground(bool enable, string? imagePath)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (enable && !string.IsNullOrWhiteSpace(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    var uri = imagePath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                        ? new Uri(imagePath)
                        : new Uri($"file:///{imagePath.Replace("\\", "/")}");
                    
                    var brush = new Microsoft.UI.Xaml.Media.ImageBrush
                    {
                        ImageSource = new BitmapImage(uri),
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill,
                        AlignmentX = Microsoft.UI.Xaml.Media.AlignmentX.Center,
                        AlignmentY = Microsoft.UI.Xaml.Media.AlignmentY.Center
                    };
                    ContentFrame.Background = brush;
                }
                catch
                {
                    // 如果加载失败，保持原背景
                }
            }
            else
            {
                ContentFrame.Background = null;
            }
        });
    }

    public void UpdateServerStatus(bool isRunning, string? serverName = null)
    {
        // 状态栏已移除
        /*
        DispatcherQueue.TryEnqueue(() =>
        {
            ServerStatusIndicator.Fill = isRunning 
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            ServerStatusText.Text = isRunning 
                ? $"服务器: {serverName ?? "运行中"}"
                : "服务器: 未运行";
        });
        */
    }

    public void UpdateFrpStatus(bool isConnected)
    {
        // 状态栏已移除
        /*
        DispatcherQueue.TryEnqueue(() =>
        {
            FrpStatusIndicator.Fill = isConnected
                ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
                : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
            FrpStatusText.Text = isConnected ? "FRP: 已连接" : "FRP: 未连接";
        });
        */
    }

    public void SetStatus(string message)
    {
        // 状态栏已移除
        /*
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = message;
        });
        */
    }

    public void NavigateToPage(string pageTag)
    {
        Type? pageType = pageTag switch
        {
            "Home" => typeof(HomePage),
            "ServerCore" => typeof(ServerCorePage),
            "MyServer" => typeof(MyServerPage),
            "LinuxNode" => typeof(LinuxNodePage),
            "NodeSelection" => typeof(NodeSelectionPage),
            "LocalJava" => typeof(LocalJavaManagePage),
            "Frp" => typeof(FrpPage),
            "MeFrp" => typeof(MeFrpPage),
            "Documentation" => typeof(DocumentationPage),
            "Settings" => typeof(SettingsPage),
            "UserProfile" => typeof(UserProfilePage),
            _ => null
        };

        if (pageType != null)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    public void NavigateToPage(string pageTag, object parameter)
    {
        Type? pageType = pageTag switch
        {
            "Home" => typeof(HomePage),
            "ServerCore" => typeof(ServerCorePage),
            "MyServer" => typeof(MyServerPage),
            "LinuxNode" => typeof(LinuxNodePage),
            "NodeSelection" => typeof(NodeSelectionPage),
            "LocalJava" => typeof(LocalJavaManagePage),
            "Frp" => typeof(FrpPage),
            "MeFrp" => typeof(MeFrpPage),
            "Documentation" => typeof(DocumentationPage),
            "Settings" => typeof(SettingsPage),
            "UserProfile" => typeof(UserProfilePage),
            _ => null
        };

        if (pageType != null)
        {
            ContentFrame.Navigate(pageType, parameter);
        }
    }

    // ============ FRPC 下载对话框方法 ============
    
    public void ShowFrpcDownloadDialog()
    {
        GlobalDownloadDialogOverlay.Visibility = Visibility.Visible;
        DownloadButton.IsEnabled = true;
    }

    public void HideFrpcDownloadDialog()
    {
        GlobalDownloadDialogOverlay.Visibility = Visibility.Collapsed;
    }

    public void UpdateDownloadProgress(double progress)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DownloadProgressBar.Value = progress;
        });
    }

    public void UpdateDownloadStatus(string status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DownloadStatusText.Text = status;
        });
    }

    public void UpdateDownloadUI(bool isDownloading)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (isDownloading)
            {
                DownloadProgressPanel.Visibility = Visibility.Visible;
                DownloadButton.IsEnabled = false;
                CancelButton.IsEnabled = false;
            }
            else
            {
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                DownloadButton.IsEnabled = true;
                CancelButton.IsEnabled = true;
            }
        });
    }

    private async void DownloadButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        
        // 订阅下载事件
        _frpcDownloadService.ProgressChanged += FrpcDownloadService_ProgressChanged;
        _frpcDownloadService.StateChanged += FrpcDownloadService_StateChanged;
        
        await _frpcDownloadService.CheckAndDownloadAsync();
        
        // 取消订阅
        _frpcDownloadService.ProgressChanged -= FrpcDownloadService_ProgressChanged;
        _frpcDownloadService.StateChanged -= FrpcDownloadService_StateChanged;
    }

    private void CancelButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_frpcDownloadService.IsDownloading)
        {
            HideFrpcDownloadDialog();
        }
    }

    private void FrpcDownloadService_ProgressChanged(object? sender, Services.FrpcDownloadProgressEventArgs e)
    {
        UpdateDownloadProgress(e.Progress);
    }

    private async void FrpcDownloadService_StateChanged(object? sender, Services.FrpcDownloadStateChangedEventArgs e)
    {
        UpdateDownloadStatus(e.Message);
        UpdateDownloadUI(e.IsDownloading);
        
        if (e.IsComplete)
        {
            // 下载完成后延迟关闭对话框
            await Task.Delay(1000);
            HideFrpcDownloadDialog();
        }
    }
}
