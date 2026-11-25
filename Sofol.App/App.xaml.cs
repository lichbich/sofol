using System;
using Microsoft.UI.Xaml;
using Sofol.App.ViewModels;
using Sofol.Core.Models;

namespace Sofol.App;

public partial class App : Application
{
    private static System.Threading.Mutex? _instanceMutex;
    private const string MutexName = "Global\\Sofol_App_SingleInstance_Mutex";
    private const string ShowInstanceMessage = "Sofol_App_ShowInstance_Message";

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private Window? _window;

    public static MainViewModel ViewModel { get; } = new();

    public static Window? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmdArgs = Environment.GetCommandLineArgs();
        bool isSetup = false;
        bool isUninstall = false;
        bool isStartupLaunch = false;

        foreach (string arg in cmdArgs)
        {
            if (arg.Equals("--setup", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--install", StringComparison.OrdinalIgnoreCase))
            {
                isSetup = true;
            }
            else if (arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                isUninstall = true;
            }
            else if (arg.Equals(Helpers.InstallationHelper.StartupArgument, StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
            {
                isStartupLaunch = true;
            }
        }

        string currentExeName = System.IO.Path.GetFileName(Environment.ProcessPath ?? "");
        if (currentExeName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
        {
            isSetup = true;
        }

        if (isUninstall)
        {
            var setupWin = new Views.SetupWindow(isUninstallMode: true);
            _window = setupWin;
            ApplyThemeToWindow(setupWin, ViewModel.SelectedThemeIndex);
            setupWin.Activate();
            return;
        }

        if (isSetup)
        {
            var setupWin = new Views.SetupWindow(isUninstallMode: false);
            _window = setupWin;
            ApplyThemeToWindow(setupWin, ViewModel.SelectedThemeIndex);
            setupWin.Activate();
            return;
        }

        bool isNewInstance;
        try
        {
            _instanceMutex = new System.Threading.Mutex(true, MutexName, out isNewInstance);
        }
        catch
        {
            isNewInstance = false;
        }

        if (!isNewInstance)
        {
            uint msg = RegisterWindowMessageW(ShowInstanceMessage);
            PostMessageW((IntPtr)0xFFFF, msg, IntPtr.Zero, IntPtr.Zero);
            Environment.Exit(0);
            return;
        }

        Helpers.InstallationHelper.EnsureStartupEntryUsesFlag();

        var mainWindow = new MainWindow();
        _window = mainWindow;
        MainWindowInstance = mainWindow;
        ApplyTheme(ViewModel.SelectedThemeIndex);

        if (isStartupLaunch && !ViewModel.ShowWindowOnWindowsStartup)
        {
            mainWindow.StartHiddenInTray();
        }
        else
        {
            mainWindow.Activate();
        }
    }

    public static void ApplyTheme(int themeIndex)
    {
        ApplyThemeToWindow(MainWindowInstance, themeIndex);
        ApplyThemeToWindow(Views.GuideWindow.CurrentInstance, themeIndex);
        ApplyThemeToWindow(Views.TrayPopupWindow.CurrentInstance, themeIndex);
    }

    public static void ApplyThemeToWindow(Window? window, int themeIndex)
    {
        if (window?.Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = (AppThemePreference)themeIndex switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    public static void NavigateMainWindow(string tag)
    {
        if (MainWindowInstance is MainWindow mainWindow)
        {
            mainWindow.NavigateToTag(tag);
            mainWindow.Activate();
        }
    }
}
