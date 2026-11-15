using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Sofol.App.ViewModels;

namespace Sofol.App.Views;

public sealed partial class SettingsPage : Page
{
    public MainViewModel ViewModel => App.ViewModel;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void OnOpenStorageClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenStorageDirectoryInExplorer();
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.CheckForUpdatesAsync(silent: false);
    }

    private async void OnApplyUpdateClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ApplyUpdateAsync();
    }
}
