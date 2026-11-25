using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sofol.App.ViewModels;
using Windows.Storage.Pickers;

namespace Sofol.App.Views;

public sealed partial class WatcherPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public WatcherPage()
    {
        InitializeComponent();
    }

    private async void OnAddCustomFolderClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindowInstance is null)
        {
            return;
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add("*");

        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await ViewModel.AddWatchedFolderAsync(folder.Path, selectInRulesTab: false);
        }
    }

    private async void OnQuickAddFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string key)
        {
            await ViewModel.AddSpecialWatchedFolderAsync(key);
        }
    }

    private void OnFolderEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.Tag is WatchedFolderItemViewModel item)
        {
            if (item.IsEnabled != toggle.IsOn)
            {
                item.IsEnabled = toggle.IsOn;
            }
        }
    }

    private async void OnPreviewFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is WatchedFolderItemViewModel item)
        {
            await ViewModel.PreviewWatchedFolderAsync(item);
        }
    }

    private async void OnRunFolderNowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is WatchedFolderItemViewModel item)
        {
            await ViewModel.RunSingleWatchedFolderNowAsync(item);
        }
    }

    private void OnOpenSingleFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is WatchedFolderItemViewModel item)
        {
            MainViewModel.OpenFolderInExplorer(item.FolderPath);
        }
    }

    private void OnRemoveWatchedFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is WatchedFolderItemViewModel item)
        {
            ViewModel.RemoveWatchedFolder(item);
        }
    }

    private void OnOpenFolderRulesClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is WatchedFolderItemViewModel item)
        {
            ViewModel.NavigateToFolderRules(item.FolderPath);
        }
    }
}
