using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sofol.App.Helpers;
using Sofol.App.Views;
using Windows.Graphics;

namespace Sofol.App;

public sealed partial class MainWindow : Window
{
    private IntPtr _hwnd;
    private AppWindow? _appWindow;
    private SystemTrayHelper? _trayHelper;
    private string _currentTag = "dashboard";
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
        Activated += OnMainWindowActivated;
        Closed += OnWindowClosed;

        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));

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

        NavigationViewItem? targetItem = FindNavItemByTag(NavView.MenuItems, tag)
                                       ?? FindNavItemByTag(NavView.FooterMenuItems, tag);
        if (targetItem is not null && !ReferenceEquals(NavView.SelectedItem, targetItem))
        {
            NavView.SelectedItem = targetItem;
        }
        else
        {
            NavigateCore(tag);
        }
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

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag && tag == "guide")
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                GuideWindow.ShowGuide("getting_started");
            });
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
            NavigationViewItem? prevItem = FindNavItemByTag(NavView.MenuItems, _currentTag)
                                         ?? FindNavItemByTag(NavView.FooterMenuItems, _currentTag);
            if (prevItem is not null)
            {
                _isRestoringSelection = true;
                NavView.SelectedItem = prevItem;
                _isRestoringSelection = false;
            }

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                GuideWindow.ShowGuide("getting_started");
            });
            return;
        }

        NavigateCore(tag);
    }

    private void NavigateCore(string tag)
    {
        Type? pageType = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "rules" => typeof(RulesPage),
            "watcher" => typeof(WatcherPage),
            "history" => typeof(HistoryPage),
            "settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType is not null)
        {
            _currentTag = tag;
            if (ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isExiting = true;
        App.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        TrayPopupWindow.ClosePermanently();
        GuideWindow.CloseIfOpen();
        _trayHelper?.Dispose();
        _trayHelper = null;

        EfficiencyModeHelper.RestoreNormalMode();
        App.ViewModel.Shutdown();
    }
}
