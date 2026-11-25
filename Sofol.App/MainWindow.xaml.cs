using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Sofol.App.Helpers;
using Sofol.App.ViewModels;
using Sofol.App.Views;
using Sofol.Core.Models;
using Windows.Graphics;
using Windows.System;

namespace Sofol.App;

public sealed partial class MainWindow : Window
{
    private const string FolderTagPrefix = "folder:";

    private readonly record struct NavEntry(string Tag, string? FolderPath);

    private readonly Stack<NavEntry> _backStack = new();
    private readonly Stack<NavEntry> _forwardStack = new();

    private IntPtr _hwnd;
    private AppWindow? _appWindow;
    private SystemTrayHelper? _trayHelper;
    private string _currentTag = "watcher";
    private NavEntry _currentEntry = new("watcher", null);
    private long _lastNavGestureTick;
    private bool _isRestoringSelection;
    private bool _isExiting;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public bool IsMainWindowVisible => _appWindow?.IsVisible == true;

    public IntPtr WindowHandle => _hwnd;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindowSizeAndPosition();
        InitializeSystemTray();

        App.ViewModel.AttachDispatcher(DispatcherQueue);
        App.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        App.ViewModel.WatchedFolderItems.CollectionChanged += OnWatchedFoldersCollectionChanged;
        Activated += OnMainWindowActivated;
        Closed += OnWindowClosed;

        WindowRootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnWindowPointerPressed), true);
        WindowRootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnWindowKeyDown), true);

        RebuildSidebarFolderItems();

        _currentTag = "watcher";
        _currentEntry = new NavEntry("watcher", null);
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(WatcherPage));
        InlineBackButton.IsEnabled = false;
        UpdateGlobalUpdateNotificationUi();

        _ = App.ViewModel.InitializeAsync();
    }

    private void ConfigureWindowSizeAndPosition()
    {
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.ico");
        if (System.IO.File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        _appWindow.Resize(new SizeInt32(1320, 840));
        _appWindow.Changed += OnAppWindowChanged;
        _appWindow.Closing += OnAppWindowClosing;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 980;
            presenter.PreferredMinimumHeight = 640;
        }

        DisplayArea? displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            RectInt32 workArea = displayArea.WorkArea;
            int x = workArea.X + Math.Max(0, (workArea.Width - 1320) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - 840) / 2);
            _appWindow.Move(new PointInt32(x, y));
        }
    }

    private void InitializeSystemTray()
    {
        string iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.ico");
        _trayHelper = new SystemTrayHelper(_hwnd, iconPath, GetTrayTooltipText());
        _trayHelper.ShowInstanceRequested += () => DispatcherQueue.TryEnqueue(RestoreFromTray);
        _trayHelper.TrayIconClicked += (x, y) => DispatcherQueue.TryEnqueue(() => TrayPopupWindow.ToggleAtTray(x, y));
        _trayHelper.TrayIconDoubleClicked += () => DispatcherQueue.TryEnqueue(RestoreFromTray);
        _trayHelper.ContextMenuOpening += () => DispatcherQueue.TryEnqueue(() => TrayPopupWindow.CurrentInstance?.HidePopup());
        _trayHelper.OpenAppRequested += () => DispatcherQueue.TryEnqueue(RestoreFromTray);
        _trayHelper.ExitAppRequested += () => DispatcherQueue.TryEnqueue(ExitApplication);
    }

    private string GetTrayTooltipText() =>
        App.ViewModel.IsWatcherRunning
            ? "Sofol — Tự động sắp xếp: ĐANG BẬT"
            : "Sofol — Tự động sắp xếp: Đang tạm dừng";

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.IsWatcherRunning))
        {
            _trayHelper?.UpdateTooltip(GetTrayTooltipText());
        }
        else if (e.PropertyName is nameof(ViewModels.MainViewModel.TargetFolderPath)
                 or nameof(ViewModels.MainViewModel.SelectedRuleTabFolderPath))
        {
            DispatcherQueue.TryEnqueue(SyncSidebarSelection);
        }
        else if (e.PropertyName is nameof(ViewModels.MainViewModel.HasUpdateAvailable)
                 or nameof(ViewModels.MainViewModel.IsGlobalUpdateBannerOpen)
                 or nameof(ViewModels.MainViewModel.GlobalUpdateBannerText))
        {
            DispatcherQueue.TryEnqueue(UpdateGlobalUpdateNotificationUi);
        }
    }

    private void UpdateGlobalUpdateNotificationUi()
    {
        SettingsNavItem.InfoBadge = App.ViewModel.HasUpdateAvailable ? new InfoBadge() : null;
        bool showBanner = App.ViewModel.IsGlobalUpdateBannerOpen &&
                          !string.IsNullOrWhiteSpace(App.ViewModel.GlobalUpdateBannerText);
        GlobalUpdateBanner.Message = App.ViewModel.GlobalUpdateBannerText;
        GlobalUpdateBanner.IsOpen = showBanner;
        GlobalUpdateBanner.Visibility = showBanner ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnGlobalUpdateBannerClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        App.ViewModel.IsGlobalUpdateBannerOpen = false;
        GlobalUpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void OnOpenUpdateSettingsClick(object sender, RoutedEventArgs e)
    {
        App.ViewModel.IsGlobalUpdateBannerOpen = false;
        GlobalUpdateBanner.Visibility = Visibility.Collapsed;
        NavigateCore("settings");
    }

    private void OnWatchedFoldersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(RebuildSidebarFolderItems);
    }

    private void RebuildSidebarFolderItems()
    {
        _isRestoringSelection = true;
        try
        {
            for (int i = NavView.MenuItems.Count - 1; i >= 0; i--)
            {
                if (NavView.MenuItems[i] is NavigationViewItem item &&
                    item.Tag is string tag &&
                    tag.StartsWith(FolderTagPrefix, StringComparison.Ordinal))
                {
                    NavView.MenuItems.RemoveAt(i);
                }
            }

            bool hasFolders = App.ViewModel.WatchedFolderItems.Count > 0;
            WatchedFoldersSeparator.Visibility = hasFolders ? Visibility.Visible : Visibility.Collapsed;
            WatchedFoldersHeader.Visibility = hasFolders ? Visibility.Visible : Visibility.Collapsed;

            foreach (WatchedFolderItemViewModel folder in App.ViewModel.WatchedFolderItems)
            {
                var navItem = new NavigationViewItem
                {
                    Content = folder.DisplayName,
                    Icon = new FontIcon { Glyph = folder.IconGlyph },
                    Tag = FolderTagPrefix + folder.FolderPath
                };
                ToolTipService.SetToolTip(navItem, folder.FolderPath);
                NavView.MenuItems.Add(navItem);
            }
        }
        finally
        {
            _isRestoringSelection = false;
        }

        SyncSidebarSelection();
    }

    private void SyncSidebarSelection()
    {
        _isRestoringSelection = true;
        try
        {
            if (_currentTag is "dashboard" or "rules")
            {
                string activePath = _currentTag == "rules" && !string.IsNullOrWhiteSpace(App.ViewModel.SelectedRuleTabFolderPath)
                    ? App.ViewModel.SelectedRuleTabFolderPath
                    : App.ViewModel.TargetFolderPath;

                NavigationViewItem? folderItem = FindSidebarFolderItem(activePath);
                if (folderItem is not null)
                {
                    NavView.SelectedItem = folderItem;
                    return;
                }

                NavView.SelectedItem = FindNavItemByTag(NavView.MenuItems, "watcher");
                return;
            }

            NavigationViewItem? staticItem = FindNavItemByTag(NavView.MenuItems, _currentTag)
                                           ?? FindNavItemByTag(NavView.FooterMenuItems, _currentTag);
            if (staticItem is not null)
            {
                NavView.SelectedItem = staticItem;
            }
        }
        finally
        {
            _isRestoringSelection = false;
        }
    }

    private NavigationViewItem? FindSidebarFolderItem(string? folderPath)
    {
        string normalized = WatchedFolderProfile.NormalizePath(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        foreach (object itemObj in NavView.MenuItems)
        {
            if (itemObj is NavigationViewItem item &&
                item.Tag is string itemTag &&
                itemTag.StartsWith(FolderTagPrefix, StringComparison.Ordinal))
            {
                string itemPath = WatchedFolderProfile.NormalizePath(itemTag[FolderTagPrefix.Length..]);
                if (string.Equals(itemPath, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        // Giữ ứng dụng hiển thị bình thường trên Taskbar/Dock khi thu nhỏ (Minimize) hoặc chuyển đổi ứng dụng
    }

    public void MinimizeToTray()
    {
        if (_isExiting || _appWindow is null)
        {
            return;
        }

        _appWindow.Hide();
        EfficiencyModeHelper.EnableBackgroundEfficiencyMode();
    }

    public void StartHiddenInTray()
    {
        _appWindow?.Hide();
        EfficiencyModeHelper.EnableBackgroundEfficiencyMode();
    }

    public void RestoreFromTray()
    {
        EfficiencyModeHelper.RestoreNormalMode();

        if (_appWindow is not null)
        {
            if (!_appWindow.IsVisible)
            {
                _appWindow.Show();
            }

            if (_appWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Minimized)
            {
                presenter.Restore();
            }
        }

        Activate();
        SetForegroundWindow(_hwnd);
    }

    public void EnsureStaysVisibleAfterPopupDismiss()
    {
        if (_isExiting || !IsMainWindowVisible)
        {
            return;
        }

        const uint SwpNosize = 0x0001;
        const uint SwpNomove = 0x0002;
        const uint SwpNoactivate = 0x0010;
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNosize | SwpNomove | SwpNoactivate);
        Activate();
        SetForegroundWindow(_hwnd);
    }

    private void OnMainWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (TrayPopupWindow.CurrentInstance is TrayPopupWindow popup && popup.IsPopupOpenAndReady)
        {
            popup.DismissPopupOnly();
        }
    }

    public void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isExiting)
        {
            args.Cancel = true;
            MinimizeToTray();
        }
    }

    public void NavigateToTag(string tag)
    {
        RestoreFromTray();

        if (tag == "guide")
        {
            GuideWindow.ShowGuide("getting_started");
            return;
        }

        NavigateCore(tag);
    }

    private static NavigationViewItem? FindNavItemByTag(System.Collections.Generic.IList<object> items, string tag)
    {
        foreach (object itemObj in items)
        {
            if (itemObj is NavigationViewItem item && item.Tag is string itemTag && itemTag == tag)
            {
                return item;
            }
        }
        return null;
    }

    private void OnInlineBackClick(object sender, RoutedEventArgs e)
    {
        TryGoBack();
    }

    private void OnWindowPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerUpdateKind kind = e.GetCurrentPoint(WindowRootGrid).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.XButton1Pressed)
        {
            if (CanTriggerNavGesture() && TryGoBack())
            {
                e.Handled = true;
            }
        }
        else if (kind == PointerUpdateKind.XButton2Pressed)
        {
            if (CanTriggerNavGesture() && TryGoForward())
            {
                e.Handled = true;
            }
        }
    }

    private void OnWindowKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool altDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

        if (e.Key == VirtualKey.GoBack || (altDown && e.Key == VirtualKey.Left))
        {
            if (CanTriggerNavGesture() && TryGoBack())
            {
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.GoForward || (altDown && e.Key == VirtualKey.Right))
        {
            if (CanTriggerNavGesture() && TryGoForward())
            {
                e.Handled = true;
            }
        }
    }

    private bool CanTriggerNavGesture()
    {
        long now = Environment.TickCount64;
        if (now - _lastNavGestureTick < 150)
        {
            return false;
        }

        _lastNavGestureTick = now;
        return true;
    }

    private bool TryGoBack()
    {
        if (_backStack.Count == 0)
        {
            return false;
        }

        NavEntry previous = _backStack.Pop();
        _forwardStack.Push(_currentEntry);
        RestoreEntry(previous);
        return true;
    }

    private bool TryGoForward()
    {
        if (_forwardStack.Count == 0)
        {
            return false;
        }

        NavEntry next = _forwardStack.Pop();
        _backStack.Push(_currentEntry);
        RestoreEntry(next);
        return true;
    }

    private void RestoreEntry(NavEntry entry)
    {
        NavigateToEntry(entry, recordHistory: false);

        if (entry.Tag is "dashboard" or "rules" && !string.IsNullOrWhiteSpace(entry.FolderPath))
        {
            _ = App.ViewModel.RestoreFolderContextFromHistoryAsync(entry.FolderPath, isRulesPage: entry.Tag == "rules");
        }
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag)
        {
            if (tag == "guide")
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    GuideWindow.ShowGuide("getting_started");
                });
                return;
            }

            if (tag.StartsWith(FolderTagPrefix, StringComparison.Ordinal) && _currentTag == "rules")
            {
                string folderPath = WatchedFolderProfile.NormalizePath(tag[FolderTagPrefix.Length..]);
                NavigateToEntry(new NavEntry("dashboard", folderPath), recordHistory: true);
                _ = App.ViewModel.OpenFolderPreviewAsync(folderPath);
            }
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isRestoringSelection || args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        if (tag == "guide")
        {
            SyncSidebarSelection();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                GuideWindow.ShowGuide("getting_started");
            });
            return;
        }

        if (tag.StartsWith(FolderTagPrefix, StringComparison.Ordinal))
        {
            string folderPath = WatchedFolderProfile.NormalizePath(tag[FolderTagPrefix.Length..]);
            NavigateToEntry(new NavEntry("dashboard", folderPath), recordHistory: true);
            _ = App.ViewModel.OpenFolderPreviewAsync(folderPath);
            return;
        }

        NavigateCore(tag);
    }

    private void NavigateCore(string tag)
    {
        string? folderPath = tag switch
        {
            "dashboard" => WatchedFolderProfile.NormalizePath(App.ViewModel.TargetFolderPath),
            "rules" => WatchedFolderProfile.NormalizePath(
                !string.IsNullOrWhiteSpace(App.ViewModel.SelectedRuleTabFolderPath)
                    ? App.ViewModel.SelectedRuleTabFolderPath
                    : App.ViewModel.TargetFolderPath),
            _ => null
        };

        NavigateToEntry(new NavEntry(tag, folderPath), recordHistory: true);
    }

    private void NavigateToEntry(NavEntry target, bool recordHistory)
    {
        string? normalizedFolder = target.Tag is "dashboard" or "rules" && !string.IsNullOrWhiteSpace(target.FolderPath)
            ? WatchedFolderProfile.NormalizePath(target.FolderPath)
            : null;
        var normalizedEntry = new NavEntry(target.Tag, normalizedFolder);

        Type? pageType = normalizedEntry.Tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "rules" => typeof(RulesPage),
            "watcher" => typeof(WatcherPage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType is null)
        {
            return;
        }

        if (recordHistory && normalizedEntry != _currentEntry)
        {
            _backStack.Push(_currentEntry);
            _forwardStack.Clear();
        }

        _currentEntry = normalizedEntry;
        _currentTag = normalizedEntry.Tag;

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }

        InlineBackButton.IsEnabled = _backStack.Count > 0;
        SyncSidebarSelection();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isExiting = true;
        App.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        App.ViewModel.WatchedFolderItems.CollectionChanged -= OnWatchedFoldersCollectionChanged;

        TrayPopupWindow.ClosePermanently();
        GuideWindow.CloseIfOpen();
        _trayHelper?.Dispose();
        _trayHelper = null;

        EfficiencyModeHelper.RestoreNormalMode();
        App.ViewModel.Shutdown();
    }
}
