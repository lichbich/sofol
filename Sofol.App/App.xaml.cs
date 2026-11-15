using System;
using Microsoft.UI.Xaml;
using Sofol.App.ViewModels;
using Sofol.Core.Models;

namespace Sofol.App;

public partial class App : Application
{
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

        _window = new MainWindow();
        MainWindowInstance = _window;
        ApplyTheme(ViewModel.SelectedThemeIndex);
        _window.Activate();
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
