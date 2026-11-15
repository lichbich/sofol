using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Sofol.App.Views;

/// <summary>
/// Cửa sổ Hướng dẫn &amp; Minh họa trực quan theo phong cách Microsoft PowerToys Welcome (OOBE).
/// </summary>
public sealed partial class GuideWindow : Window
{
    public static GuideWindow? CurrentInstance { get; private set; }

    private readonly Dictionary<string, FrameworkElement> _topicPanels;

    public GuideWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(GuideTitleBar);

        ConfigureWindowSizeAndPosition();

        _topicPanels = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            ["getting_started"] = TopicGettingStarted,
            ["smart_grouping"] = TopicSmartGrouping,
            ["smart_folder_modes"] = TopicSmartFolderModes,
            ["keyword_and_extension_rules"] = TopicRulesPriority,
            ["auto_watcher"] = TopicAutoWatcher,
            ["history_and_undo"] = TopicHistoryUndo
        };

        // Đồng bộ trạng thái mô phỏng ban đầu theo cấu hình thực tế của người dùng nếu có
        if (SimParentFolderSwitch is not null && SimNestInsideRuleSwitch is not null)
        {
            SimParentFolderSwitch.IsOn = App.ViewModel.UseDedicatedSmartRootFolder;
            SimNestInsideRuleSwitch.IsOn = App.ViewModel.GroupPrefixInsideCategory;
        }

        UpdateSmartFolderSimulator();
        SelectTopic("getting_started");

        Closed += (_, _) => CurrentInstance = null;
    }

    public static void ShowGuide(string? initialTopicTag = null)
    {
        if (CurrentInstance is null)
        {
            CurrentInstance = new GuideWindow();
            App.ApplyThemeToWindow(CurrentInstance, App.ViewModel.SelectedThemeIndex);
        }

        if (!string.IsNullOrWhiteSpace(initialTopicTag))
        {
            CurrentInstance.SelectTopic(initialTopicTag);
        }

        CurrentInstance.Activate();
        CurrentInstance.BringToFront();
        CurrentInstance.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            CurrentInstance?.BringToFront();
        });
    }

    public void BringToFront()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SwRestore);
        }

        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        SetWindowPos(hwnd, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
    }

    private const int SwRestore = 9;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static void CloseIfOpen()
    {
        CurrentInstance?.Close();
        CurrentInstance = null;
    }

    public void SelectTopic(string tag)
    {
        if (!_topicPanels.ContainsKey(tag))
        {
            tag = "getting_started";
        }

        foreach (var itemObj in GuideNavView.MenuItems)
        {
            if (itemObj is NavigationViewItem navItem && navItem.Tag is string itemTag && itemTag == tag)
            {
                GuideNavView.SelectedItem = navItem;
                break;
            }
        }

        ShowPanel(tag);
    }

    private void ShowPanel(string tag)
    {
        foreach (var kvp in _topicPanels)
        {
            kvp.Value.Visibility = kvp.Key == tag ? Visibility.Visible : Visibility.Collapsed;
        }

        TopicScrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void OnGuideNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            ShowPanel(tag);
        }
    }

    private void OnSimulatorToggleChanged(object sender, RoutedEventArgs e)
    {
        UpdateSmartFolderSimulator();
    }

    private void UpdateSmartFolderSimulator()
    {
        if (SimParentFolderSwitch is null ||
            SimNestInsideRuleSwitch is null ||
            SimModeBadgeText is null ||
            SimPathKeywordText is null ||
            SimPathExtensionText is null ||
            SimPathUnmatchedText is null)
        {
            return;
        }

        bool useParent = SimParentFolderSwitch.IsOn;
        bool nestInsideRule = SimNestInsideRuleSwitch.IsOn;
        string parentName = string.IsNullOrWhiteSpace(App.ViewModel.PrefixGroupRootFolderName)
            ? "Gom theo tên"
            : App.ViewModel.PrefixGroupRootFolderName.Trim();

        SimModeBadgeText.Text = (useParent, nestInsideRule) switch
        {
            (true, true) => "Khuyên dùng: Lồng quy tắc + Thư mục cha dự phòng",
            (false, true) => "Chế độ: Lồng vào thư mục quy tắc",
            (true, false) => $"Chế độ: Gom vào thư mục cha '{parentName}'",
            (false, false) => "Chế độ: Đặt cụm thẳng ở Downloads"
        };

        SimPathKeywordText.Text = nestInsideRule
            ? "Downloads\\\n└─ Book test\\\n   └─ Book_Exported\\ (3 tệp)"
            : "Downloads\\\n└─ Book test\\\n   └─ (3 tệp đặt trực tiếp)";

        if (nestInsideRule)
        {
            SimPathExtensionText.Text = "Downloads\\\n└─ Tài liệu\\\n   └─ GGKEY_01\\ (2 tệp)";
        }
        else if (useParent)
        {
            SimPathExtensionText.Text = $"Downloads\\\n└─ {parentName}\\\n   └─ GGKEY_01\\ (2 tệp)";
        }
        else
        {
            SimPathExtensionText.Text = "Downloads\\\n└─ GGKEY_01\\ (2 tệp)";
        }

        SimPathUnmatchedText.Text = useParent
            ? $"Downloads\\\n└─ {parentName}\\\n   └─ Project\\ (2 tệp)"
            : "Downloads\\\n└─ Project\\ (2 tệp)";
    }

    private void ConfigureWindowSizeAndPosition()
    {
        const int width = 1040;
        const int height = 700;

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.ico");
        if (System.IO.File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        appWindow.Resize(new SizeInt32(width, height));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 860;
            presenter.PreferredMinimumHeight = 580;
        }

        DisplayArea? displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            RectInt32 workArea = displayArea.WorkArea;
            int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
            appWindow.Move(new PointInt32(x, y));
        }
    }

    private void OnJumpToSmartFolderModesClick(object sender, RoutedEventArgs e) => SelectTopic("smart_folder_modes");

    private void OnOpenDashboardClick(object sender, RoutedEventArgs e) => App.NavigateMainWindow("dashboard");

    private void OnOpenRulesClick(object sender, RoutedEventArgs e) => App.NavigateMainWindow("rules");

    private void OnOpenWatcherClick(object sender, RoutedEventArgs e) => App.NavigateMainWindow("watcher");

    private void OnOpenHistoryClick(object sender, RoutedEventArgs e) => App.NavigateMainWindow("history");
}
