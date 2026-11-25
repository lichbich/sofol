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
    public const string AppDisplayName = "Sofol";
    public const string AppPublisher = "Sofol";
    public const string AppVersion = "1.2.1";
    public const string StartupArgument = "--startup";

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] string ppszFileName);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

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
            Directory.CreateDirectory(desktopDir);
            string desktopShortcut = Path.Combine(desktopDir, "Sofol.lnk");
            CreateShortcut(desktopShortcut, installedExePath, targetDir, iconPath, "Sofol - Sắp xếp tệp thông minh");
        }

        if (createStartMenuShortcut)
        {
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            Directory.CreateDirectory(startMenuDir);
            string startMenuShortcut = Path.Combine(startMenuDir, "Sofol.lnk");
            CreateShortcut(startMenuShortcut, installedExePath, targetDir, iconPath, "Sofol - Sắp xếp tệp thông minh");
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
            var link = (IShellLinkW)new ShellLinkClass();
            link.SetPath(targetExePath);
            link.SetWorkingDirectory(workingDir);
            link.SetDescription(description);
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                link.SetIconLocation(iconPath, 0);
            }
            var persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, false);

            SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"COM shortcut error: {ex.Message}. Using PowerShell fallback...");
            try
            {
                string ps = $"$w = New-Object -ComObject WScript.Shell; $s = $w.CreateShortcut('{shortcutPath}'); $s.TargetPath = '{targetExePath}'; $s.WorkingDirectory = '{workingDir}'; $s.IconLocation = '{iconPath},0'; $s.Description = '{description}'; $s.Save();";
                using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{ps}\"") { CreateNoWindow = true, UseShellExecute = false });
                p?.WaitForExit(3000);

                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception fallbackEx)
            {
                Debug.WriteLine($"PowerShell shortcut fallback failed: {fallbackEx.Message}");
            }
        }
    }

    public static void SetRunAtStartup(bool enable, string? exePath = null)
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
                string resolvedExe = string.IsNullOrWhiteSpace(exePath)
                    ? (Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Sofol.App.exe"))
                    : exePath;
                key.SetValue(AppName, $"\"{resolvedExe}\" {StartupArgument}");
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

    public static void EnsureStartupEntryUsesFlag()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key?.GetValue(AppName) is string raw &&
                !string.IsNullOrWhiteSpace(raw) &&
                !raw.Contains(StartupArgument, StringComparison.OrdinalIgnoreCase))
            {
                string cleanExe = raw.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(cleanExe))
                {
                    key.SetValue(AppName, $"\"{cleanExe}\" {StartupArgument}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lỗi chuẩn hóa khóa khởi động: {ex.Message}");
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
            foreach (string name in new[] { "Sofol.lnk", "Sofol - Trình Sắp Xếp Tệp Thông Minh.lnk" })
            {
                string sc = Path.Combine(desktopDir, name);
                if (File.Exists(sc)) File.Delete(sc);
            }

            // Xóa lối tắt Start Menu
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            foreach (string name in new[] { "Sofol.lnk", "Sofol - Trình Sắp Xếp Tệp Thông Minh.lnk" })
            {
                string sc = Path.Combine(startMenuDir, name);
                if (File.Exists(sc)) File.Delete(sc);
            }

            SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

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
