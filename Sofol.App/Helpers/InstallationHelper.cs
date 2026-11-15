using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Sofol.App.Helpers;

/// <summary>
/// Hỗ trợ cài đặt, gỡ cài đặt, tạo lối tắt Windows và tích hợp khởi động cùng hệ thống.
/// </summary>
public static class InstallationHelper
{
    public const string AppName = "Sofol";
    public const string AppDisplayName = "Sofol - Trình Sắp Xếp Tệp Thông Minh";
    public const string AppPublisher = "Sofol";
    public const string AppVersion = "1.0.0";

    public static string GetDefaultInstallationDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Programs", AppName);
    }

    public static bool IsRunningFromInstalledLocation()
    {
        string currentExeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string installedDir = GetDefaultInstallationDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(currentExeDir, installedDir, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task InstallAppAsync(
        string targetDir,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        bool runAtStartup,
        IProgress<(double percent, string status)> progress)
    {
        progress.Report((0.10, "Đang chuẩn bị thư mục cài đặt..."));
        await Task.Delay(100);

        Directory.CreateDirectory(targetDir);

        string sourceDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string[] allFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
        int totalFiles = allFiles.Length;

        for (int i = 0; i < totalFiles; i++)
        {
            string srcFile = allFiles[i];
            string relativePath = Path.GetRelativePath(sourceDir, srcFile);
            string destFile = Path.Combine(targetDir, relativePath);

            string? destSubDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(destSubDir) && !Directory.Exists(destSubDir))
            {
                Directory.CreateDirectory(destSubDir);
            }

            // Sao chép tệp chương trình (bỏ qua nếu chính file đang chạy tại đích)
            if (!string.Equals(srcFile, destFile, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(srcFile, destFile, overwrite: true);
            }

            double percent = 0.15 + (0.65 * ((double)(i + 1) / totalFiles));
            progress.Report((percent, $"Đang sao chép ({i + 1}/{totalFiles}): {Path.GetFileName(srcFile)}"));
        }

        string installedExePath = Path.Combine(targetDir, "Sofol.App.exe");
        string iconPath = Path.Combine(targetDir, "Assets", "AppLogo.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = installedExePath;
        }

        progress.Report((0.85, "Đang tạo lối tắt và liên kết hệ thống..."));
        await Task.Delay(100);

        if (createDesktopShortcut)
        {
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string desktopShortcut = Path.Combine(desktopDir, $"{AppDisplayName}.lnk");
            CreateShortcut(desktopShortcut, installedExePath, targetDir, iconPath, AppDisplayName);
        }

        if (createStartMenuShortcut)
        {
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            string startMenuShortcut = Path.Combine(startMenuDir, $"{AppDisplayName}.lnk");
            CreateShortcut(startMenuShortcut, installedExePath, targetDir, iconPath, AppDisplayName);
        }

        progress.Report((0.92, "Đang đăng ký cấu hình Windows..."));
        SetRunAtStartup(runAtStartup, installedExePath);
        RegisterUninstallEntry(targetDir, installedExePath, iconPath);

        progress.Report((1.0, "Cài đặt hoàn tất thành công!"));
        await Task.Delay(200);
    }

    public static void CreateShortcut(string shortcutPath, string targetExePath, string workingDir, string iconPath, string description)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExePath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.IconLocation = $"{iconPath},0";
            shortcut.Description = description;
            shortcut.Save();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Không thể tạo lối tắt {shortcutPath}: {ex.Message}");
        }
    }

    public static void SetRunAtStartup(bool enable, string exePath)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null)
            {
                return;
            }

            if (enable)
            {
                key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lỗi cấu hình khởi động cùng Windows: {ex.Message}");
        }
    }

    public static bool IsRunAtStartupEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void RegisterUninstallEntry(string installDir, string exePath, string iconPath)
    {
        try
        {
            using RegistryKey? uninstallKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Sofol");
            if (uninstallKey is null)
            {
                return;
            }

            uninstallKey.SetValue("DisplayName", AppDisplayName);
            uninstallKey.SetValue("DisplayIcon", $"{iconPath},0");
            uninstallKey.SetValue("DisplayVersion", AppVersion);
            uninstallKey.SetValue("Publisher", AppPublisher);
            uninstallKey.SetValue("InstallLocation", installDir);
            uninstallKey.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
            uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lỗi đăng ký gỡ cài đặt: {ex.Message}");
        }
    }

    public static void UnregisterUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Sofol", throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lỗi xóa đăng ký gỡ cài đặt: {ex.Message}");
        }
    }

    public static void UninstallApp(string installDir)
    {
        try
        {
            // Xóa lối tắt Desktop
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string desktopShortcut = Path.Combine(desktopDir, $"{AppDisplayName}.lnk");
            if (File.Exists(desktopShortcut))
            {
                File.Delete(desktopShortcut);
            }

            // Xóa lối tắt Start Menu
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            string startMenuShortcut = Path.Combine(startMenuDir, $"{AppDisplayName}.lnk");
            if (File.Exists(startMenuShortcut))
            {
                File.Delete(startMenuShortcut);
            }

            // Tắt khởi động cùng Windows & Xóa registry
            SetRunAtStartup(false, string.Empty);
            UnregisterUninstallEntry();

            // Lập lịch xóa thư mục cài đặt sau khi tiến trình hiện tại thoát
            int currentPid = Process.GetCurrentProcess().Id;
            string cleanupScript = Path.Combine(Path.GetTempPath(), "sofol_uninstall.cmd");
            string scriptContent = $@"@echo off
timeout /t 2 /nobreak > nul
:WAIT_LOOP
tasklist /fi ""PID eq {currentPid}"" 2>nul | find ""{currentPid}"" > nul
if not errorlevel 1 (
    timeout /t 1 /nobreak > nul
    goto WAIT_LOOP
)
rmdir /s /q ""{installDir}""
del ""%~f0""
";
            File.WriteAllText(cleanupScript, scriptContent);

            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{cleanupScript}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lỗi gỡ cài đặt: {ex.Message}");
        }
    }
}
