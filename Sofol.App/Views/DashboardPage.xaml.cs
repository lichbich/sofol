using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sofol.App.ViewModels;
using Windows.Storage.Pickers;

namespace Sofol.App.Views;

public sealed partial class DashboardPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public DashboardPage()
    {
        InitializeComponent();
    }

    private void OnBreadcrumbWorkspaceClick(object sender, RoutedEventArgs e) =>
        ViewModel.NavigateBackToWorkspace();

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

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        ViewModel.OpenTargetFolderInExplorer();
}
