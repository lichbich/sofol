using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Sofol.App.Helpers;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace Sofol.App.Views;

public sealed partial class SetupWindow : Window
{
    private readonly bool _isUninstallMode;
    private bool _isCompleted;
    private string _installedTargetDir = string.Empty;

    public SetupWindow(bool isUninstallMode = false)
    {
        _isUninstallMode = isUninstallMode;
        InitializeComponent();

        ConfigureWindow();
        InitializeView();
    }

    private void ConfigureWindow()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        appWindow.Resize(new SizeInt32(640, 540));

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        DisplayArea? displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            RectInt32 workArea = displayArea.WorkArea;
            int x = workArea.X + Math.Max(0, (workArea.Width - 640) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - 540) / 2);
            appWindow.Move(new PointInt32(x, y));
        }
    }

    private void InitializeView()
    {
        VersionBadgeText.Text = $"v{InstallationHelper.AppVersion}";
        if (_isUninstallMode)
        {
            Title = "Gỡ cài đặt Sofol";
            InstallPanel.Visibility = Visibility.Collapsed;
            UninstallPanel.Visibility = Visibility.Visible;
            ActionText.Text = "Gỡ cài đặt ngay";
            ActionIcon.Glyph = "\uE74D";
        }
        else
        {
            Title = "Cài đặt Sofol - Trình Sắp Xếp Tệp Thông Minh";
            InstallPathTextBox.Text = InstallationHelper.GetDefaultInstallationDirectory();
            InstallPanel.Visibility = Visibility.Visible;
            UninstallPanel.Visibility = Visibility.Collapsed;
            ActionText.Text = "Cài đặt ngay";
            ActionIcon.Glyph = "\uE896";
        }
    }

    private async void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            InstallPathTextBox.Text = Path.Combine(folder.Path, "Sofol");
        }
    }

    private async void OnActionButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            // Nếu đã cài đặt xong, bấm nút sẽ khởi chạy ứng dụng chính và đóng SetupWindow
            if (!_isUninstallMode && !string.IsNullOrEmpty(_installedTargetDir))
            {
                string targetExe = Path.Combine(_installedTargetDir, "Sofol.App.exe");
                if (File.Exists(targetExe))
                {
                    Process.Start(new ProcessStartInfo(targetExe) { UseShellExecute = true });
                }
            }

            Close();
            return;
        }

        if (_isUninstallMode)
        {
            await RunUninstallAsync();
        }
        else
        {
            await RunInstallAsync();
        }
    }

    private async Task RunInstallAsync()
    {
        string targetDir = InstallPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            targetDir = InstallationHelper.GetDefaultInstallationDirectory();
        }

        _installedTargetDir = targetDir;
        bool desktop = DesktopShortcutCheckBox.IsChecked == true;
        bool startMenu = StartMenuShortcutCheckBox.IsChecked == true;
        bool startup = RunAtStartupCheckBox.IsChecked == true;

        // Vô hiệu hóa điều khiển để tránh click nhiều lần
        InstallPathTextBox.IsEnabled = false;
        BrowseFolderButton.IsEnabled = false;
        DesktopShortcutCheckBox.IsEnabled = false;
        StartMenuShortcutCheckBox.IsEnabled = false;
        RunAtStartupCheckBox.IsEnabled = false;
        ActionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        ProgressArea.Visibility = Visibility.Visible;

        var progress = new Progress<(double percent, string status)>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InstallProgressBar.Value = p.percent;
                StatusTextBlock.Text = p.status;
            });
        });

        try
        {
            await Task.Run(() => InstallationHelper.InstallAppAsync(targetDir, desktop, startMenu, startup, progress));

            _isCompleted = true;
            ActionButton.IsEnabled = true;
            CancelButton.Visibility = Visibility.Collapsed;
            ActionText.Text = "Khởi chạy Sofol ngay";
            ActionIcon.Glyph = "\uE768";
            StatusTextBlock.Text = "Cài đặt thành công! Bạn có thể khởi chạy Sofol ngay bây giờ.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Cài đặt thất bại: {ex.Message}";
            ActionButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private async Task RunUninstallAsync()
    {
        ActionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ProgressArea.Visibility = Visibility.Visible;
        InstallProgressBar.IsIndeterminate = true;
        StatusTextBlock.Text = "Đang gỡ cài đặt và dọn dẹp lối tắt...";

        await Task.Delay(500);

        string currentDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        await Task.Run(() => InstallationHelper.UninstallApp(currentDir));

        _isCompleted = true;
        ActionButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
        ActionText.Text = "Hoàn tất & Đóng";
        ActionIcon.Glyph = "\uE8FB";
        StatusTextBlock.Text = "Đã gỡ cài đặt Sofol thành công.";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
