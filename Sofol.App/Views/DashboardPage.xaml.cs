using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Sofol.App.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;

namespace Sofol.App.Views;

public sealed partial class DashboardPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public DashboardPage()
    {
        InitializeComponent();
    }

    private async void OnBrowseFolderClick(object sender, RoutedEventArgs e)
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
            await ViewModel.SetTargetFolderAndAnalyzeAsync(folder.Path);
        }
    }

    private async void OnFolderPathKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await ViewModel.SetTargetFolderAndAnalyzeAsync(FolderPathTextBox.Text);
        }
    }

    private async void OnQuickDownloadsClick(object sender, RoutedEventArgs e) =>
        await ViewModel.SelectSpecialFolderAsync(Environment.SpecialFolder.UserProfile);

    private async void OnQuickDesktopClick(object sender, RoutedEventArgs e) =>
        await ViewModel.SelectSpecialFolderAsync(Environment.SpecialFolder.DesktopDirectory);

    private async void OnQuickDocumentsClick(object sender, RoutedEventArgs e) =>
        await ViewModel.SelectSpecialFolderAsync(Environment.SpecialFolder.MyDocuments);

    private async void OnCreateDemoSandboxClick(object sender, RoutedEventArgs e) =>
        await ViewModel.CreateDemoSandboxAsync();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenTargetFolderInExplorer();
}
