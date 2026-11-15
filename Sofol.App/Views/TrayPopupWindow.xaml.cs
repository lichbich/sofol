using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Sofol.App.ViewModels;
using Sofol.Core.Models;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace Sofol.App.Views;

/// <summary>
/// Bảng điều khiển nhanh bằng WinUI 3 hiện lên khi bấm vào biểu tượng Sofol dưới khay hệ thống.
/// </summary>
public sealed partial class TrayPopupWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaTransitionsForceDisabled = 3;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private static TrayPopupWindow? _instance;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueueTimer _outsideClickTimer;
    private Storyboard? _openStoryboard;
    private int _anchorX;
    private int _anchorY;
    private bool _hasExplicitAnchor;
    private bool _allowClose;
    private bool _mainWindowWasVisibleOnOpen;
    private long _lastDeactivatedTick;
    private long _suppressDeactivateUntilTick;

    public MainViewModel ViewModel => App.ViewModel;

    public static TrayPopupWindow? CurrentInstance => _instance;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    private TrayPopupWindow()
    {
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _outsideClickTimer = DispatcherQueue.CreateTimer();
        _outsideClickTimer.Interval = TimeSpan.FromMilliseconds(45);
        _outsideClickTimer.Tick += OnOutsideClickTimerTick;

        ConfigureWindowChrome();

        _appWindow.Closing += OnAppWindowClosing;
        Activated += OnWindowActivated;
    }

    public static void ToggleAtTray(int screenX, int screenY)
    {
        EnsureCreated();
        if (_instance is null)
        {
            return;
        }

        // Nếu người dùng vừa bấm lên icon khay để đóng popup đang mở thì không mở lại ngay lập tức
        if (_instance.IsPopupVisible || (Environment.TickCount64 - _instance._lastDeactivatedTick) < 300)
        {
            _instance.HidePopup();
            return;
        }

        _instance.ShowNearPoint(screenX, screenY);
    }

    public static void ShowAtDefaultCorner()
    {
        EnsureCreated();
        _instance?.ShowNearPoint(0, 0, useWorkAreaBottomRight: true);
    }

    public static void ClosePermanently()
    {
        if (_instance is null)
        {
            return;
        }

        _instance._allowClose = true;
        _instance.Close();
        _instance = null;
    }

    private bool IsPopupVisible => _appWindow.IsVisible;

    public bool IsPopupOpenAndReady => IsPopupVisible && Environment.TickCount64 >= _suppressDeactivateUntilTick;

    private static void EnsureCreated()
    {
        if (_instance is null)
        {
            _instance = new TrayPopupWindow();
            App.ApplyThemeToWindow(_instance, App.ViewModel.SelectedThemeIndex);
        }
    }

    private void ConfigureWindowChrome()
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }

        long exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow));

        int cornerPreference = DwmwcpRound;
        DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        int disableTransitions = 0;
        DwmSetWindowAttribute(_hwnd, DwmwaTransitionsForceDisabled, ref disableTransitions, sizeof(int));
    }

    public void ShowNearPoint(int screenX, int screenY, bool useWorkAreaBottomRight = false)
    {
        _anchorX = screenX;
        _anchorY = screenY;
        _hasExplicitAnchor = !useWorkAreaBottomRight && !(screenX == 0 && screenY == 0);
        _mainWindowWasVisibleOnOpen = App.MainWindowInstance is MainWindow mw && mw.IsMainWindowVisible;

        App.ApplyThemeToWindow(this, App.ViewModel.SelectedThemeIndex);
        FeedbackBorder.Visibility = Visibility.Collapsed;
        QuickRuleErrorText.Visibility = Visibility.Collapsed;

        Bindings.Update();
        ResizeAndPositionPopup();
        _suppressDeactivateUntilTick = Environment.TickCount64 + 450;

        _openStoryboard?.Stop();
        RootGrid.Opacity = 0.0;
        PopupSlideTransform.TranslateY = 20.0;

        _appWindow.Show();
        ForceActivatePopup();
        PlayEntranceAnimation();
        _outsideClickTimer.Start();
    }

    public void HidePopup()
    {
        _outsideClickTimer.Stop();
        if (_appWindow.IsVisible)
        {
            _appWindow.Hide();
        }
    }

    public void DismissPopupOnly()
    {
        HidePopup();
        if (_mainWindowWasVisibleOnOpen && App.MainWindowInstance is MainWindow mw && mw.IsMainWindowVisible)
        {
            mw.EnsureStaysVisibleAfterPopupDismiss();
        }
    }

    private void ForceActivatePopup()
    {
        IntPtr fg = GetForegroundWindow();
        uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
        uint curThread = GetCurrentThreadId();
        bool attached = false;

        try
        {
            if (fgThread != 0 && fgThread != curThread)
            {
                attached = AttachThreadInput(curThread, fgThread, true);
            }

            BringWindowToTop(_hwnd);
            SetForegroundWindow(_hwnd);
            Activate();
            SetFocus(_hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(curThread, fgThread, false);
            }
        }
    }

    private void PlayEntranceAnimation()
    {
        _openStoryboard?.Stop();

        RootGrid.Opacity = 0.0;
        PopupSlideTransform.TranslateY = 20.0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(opacityAnim, RootGrid);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var slideAnim = new DoubleAnimation
        {
            From = 20.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(210)),
            EasingFunction = ease
        };
        Storyboard.SetTarget(slideAnim, PopupSlideTransform);
        Storyboard.SetTargetProperty(slideAnim, "TranslateY");

        var sb = new Storyboard();
        sb.Children.Add(opacityAnim);
        sb.Children.Add(slideAnim);
        _openStoryboard = sb;
        sb.Begin();
    }

    private void OnOutsideClickTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (!IsPopupVisible)
        {
            _outsideClickTimer.Stop();
            return;
        }

        if (Environment.TickCount64 < _suppressDeactivateUntilTick)
        {
            return;
        }

        bool mouseDown = (GetAsyncKeyState(0x01) & 0x8000) != 0 ||
                         (GetAsyncKeyState(0x02) & 0x8000) != 0 ||
                         (GetAsyncKeyState(0x04) & 0x8000) != 0;
        if (!mouseDown)
        {
            return;
        }

        if (GetCursorPos(out POINT pt) && GetWindowRect(_hwnd, out RECT wr))
        {
            bool outside = pt.X < wr.Left || pt.X > wr.Right || pt.Y < wr.Top || pt.Y > wr.Bottom;
            if (outside)
            {
                _lastDeactivatedTick = Environment.TickCount64;
                DismissPopupOnly();
            }
        }
    }

    private const double PopupLogicalWidth = 380.0;

    private void ResizeAndPositionPopup()
    {
        _suppressDeactivateUntilTick = Environment.TickCount64 + 300;
        Bindings.Update();
        RootGrid.UpdateLayout();
        RootGrid.Measure(new Size(PopupLogicalWidth, 900));

        double dpiScale = Math.Max(1.0, GetDpiForWindow(_hwnd) / 96.0);
        int clientWidthPx = (int)Math.Ceiling(PopupLogicalWidth * dpiScale);
        int clientHeightPx = (int)Math.Ceiling((RootGrid.DesiredSize.Height + 2) * dpiScale);
        clientHeightPx = Math.Clamp(clientHeightPx, (int)(180 * dpiScale), (int)(760 * dpiScale));

        _appWindow.ResizeClient(new SizeInt32(clientWidthPx, clientHeightPx));

        // Bù phần chiều cao tiêu đề mà Windows App SDK cộng thêm khi SetBorderAndTitleBar(true, false)
        if (GetWindowRect(_hwnd, out RECT wr) && GetClientRect(_hwnd, out RECT cr))
        {
            int actualClientW = cr.Right - cr.Left;
            int actualClientH = cr.Bottom - cr.Top;
            int frameW = Math.Max(0, (wr.Right - wr.Left) - actualClientW);
            int frameH = Math.Max(0, (wr.Bottom - wr.Top) - actualClientH);

            if (actualClientW != clientWidthPx || actualClientH != clientHeightPx)
            {
                _appWindow.Resize(new SizeInt32(clientWidthPx + frameW, clientHeightPx + frameH));
            }
        }

        DisplayArea? displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            return;
        }

        SizeInt32 outerSize = _appWindow.Size;
        int outerWidthPx = Math.Max(clientWidthPx, outerSize.Width);
        int outerHeightPx = Math.Max(clientHeightPx, outerSize.Height);

        RectInt32 workArea = displayArea.WorkArea;
        RectInt32 outerBounds = displayArea.OuterBounds;
        int margin = (int)(12 * dpiScale);

        int bottomLimitY = Math.Min(
            workArea.Y + workArea.Height - (int)(18 * dpiScale),
            outerBounds.Y + outerBounds.Height - (int)(68 * dpiScale));

        bool anchorValidInTray = _hasExplicitAnchor && _anchorY >= outerBounds.Y + outerBounds.Height - (int)(160 * dpiScale);
        if (anchorValidInTray)
        {
            bottomLimitY = Math.Min(bottomLimitY, _anchorY - (int)(38 * dpiScale));
        }

        int x = anchorValidInTray
            ? _anchorX - (outerWidthPx / 2)
            : workArea.X + workArea.Width - outerWidthPx - margin;

        int minX = workArea.X + margin;
        int maxX = Math.Max(minX, workArea.X + workArea.Width - outerWidthPx - margin);
        x = Math.Clamp(x, minX, maxX);

        int y = Math.Max(workArea.Y + margin, bottomLimitY - outerHeightPx);

        _appWindow.Move(new PointInt32(x, y));
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            if (Environment.TickCount64 < _suppressDeactivateUntilTick)
            {
                return;
            }

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == _hwnd || IsChild(_hwnd, fg))
            {
                return;
            }

            _lastDeactivatedTick = Environment.TickCount64;
            DismissPopupOnly();
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
            HidePopup();
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenTargetFolderInExplorer();
        HidePopup();
    }

    private async void OnQuickOrganizeClick(object sender, RoutedEventArgs e)
    {
        QuickOrganizeButton.IsEnabled = false;
        try
        {
            string resultMessage = await ViewModel.RunQuickOrganizeAsync();
            ShowFeedback(resultMessage);
        }
        finally
        {
            QuickOrganizeButton.IsEnabled = true;
        }
    }

    private void OnToggleQuickRuleClick(object sender, RoutedEventArgs e)
    {
        bool willShow = QuickRulePanel.Visibility != Visibility.Visible;
        QuickRulePanel.Visibility = willShow ? Visibility.Visible : Visibility.Collapsed;
        QuickRuleErrorText.Visibility = Visibility.Collapsed;
        ResizeAndPositionPopup();

        if (willShow)
        {
            QuickRuleNameBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnCancelQuickRuleClick(object sender, RoutedEventArgs e)
    {
        QuickRulePanel.Visibility = Visibility.Collapsed;
        QuickRuleErrorText.Visibility = Visibility.Collapsed;
        ClearQuickRuleInputs();
        ResizeAndPositionPopup();
    }

    private void OnSaveQuickRuleClick(object sender, RoutedEventArgs e)
    {
        string name = QuickRuleNameBox.Text.Trim();
        string targetFolder = QuickRuleFolderBox.Text.Trim();
        List<string> extensions = OrganizationRule.ParseCommaSeparatedExtensions(QuickRuleExtBox.Text);
        List<string> keywords = OrganizationRule.ParseCommaSeparatedKeywords(QuickRuleKeywordBox.Text);

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowQuickRuleError("Vui lòng nhập tên quy tắc.");
            return;
        }

        if (string.IsNullOrWhiteSpace(targetFolder))
        {
            ShowQuickRuleError("Vui lòng nhập tên thư mục đích.");
            return;
        }

        if (extensions.Count == 0 && keywords.Count == 0)
        {
            ShowQuickRuleError("Hãy nhập ít nhất 1 đuôi tệp hoặc 1 từ khóa nhận diện.");
            return;
        }

        var rule = new OrganizationRule
        {
            Name = name,
            TargetFolderName = targetFolder,
            Extensions = extensions,
            Keywords = keywords,
            Priority = 15,
            IconGlyph = "\uE8B7",
            IsEnabled = true
        };

        ViewModel.SaveRuleChanges(rule, isNew: true);
        QuickRulePanel.Visibility = Visibility.Collapsed;
        QuickRuleErrorText.Visibility = Visibility.Collapsed;
        ClearQuickRuleInputs();
        ShowFeedback($"Đã thêm quy tắc \"{rule.Name}\" → {rule.TargetFolderName}.");
    }

    private void ShowQuickRuleError(string error)
    {
        QuickRuleErrorText.Text = error;
        QuickRuleErrorText.Visibility = Visibility.Visible;
        ResizeAndPositionPopup();
    }

    private void ShowFeedback(string message)
    {
        FeedbackTextBlock.Text = message;
        FeedbackBorder.Visibility = Visibility.Visible;
        ResizeAndPositionPopup();
    }

    private void ClearQuickRuleInputs()
    {
        QuickRuleNameBox.Text = string.Empty;
        QuickRuleFolderBox.Text = string.Empty;
        QuickRuleExtBox.Text = string.Empty;
        QuickRuleKeywordBox.Text = string.Empty;
    }

    private void OnToggleWatcherClick(object sender, RoutedEventArgs e)
    {
        _suppressDeactivateUntilTick = Environment.TickCount64 + 350;
        ViewModel.ToggleWatcherCommand.Execute(null);
        Bindings.Update();
    }

    public Visibility ToVisible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ToCollapsed(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public Brush ToggleIconFg(bool running) => new SolidColorBrush(
        running ? Color.FromArgb(255, 196, 43, 28) : Color.FromArgb(255, 15, 123, 62));

    public Brush BadgeBg(bool running) => new SolidColorBrush(
        running ? Color.FromArgb(36, 16, 137, 62) : Color.FromArgb(28, 128, 128, 128));

    public Brush BadgeFg(bool running) => new SolidColorBrush(
        running ? Color.FromArgb(255, 14, 122, 54) : Color.FromArgb(255, 110, 118, 129));
}
