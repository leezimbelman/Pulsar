using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pulsar.Models;
using Pulsar.ViewModels;

namespace Pulsar.Pages;

public sealed partial class DownloadPage : Page
{
    public DownloadViewModel VM => App.Downloads;
    public SettingsViewModel Setup => App.SettingsVM;

    public DownloadPage()
    {
        InitializeComponent();
    }

    private static DownloadJob JobOf(object sender) => (DownloadJob)((FrameworkElement)sender).DataContext;

    private void Play_Click(object sender, RoutedEventArgs e) => App.OpenPlayer(JobOf(sender).OutputPath);
    private void ShowInFolder_Click(object sender, RoutedEventArgs e) => DownloadViewModel.ShowInFolder(JobOf(sender));
    private void Retry_Click(object sender, RoutedEventArgs e) => VM.Retry(JobOf(sender));
    private void Cancel_Click(object sender, RoutedEventArgs e) => VM.Cancel(JobOf(sender));
    private void Remove_Click(object sender, RoutedEventArgs e) => VM.Remove(JobOf(sender));

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && VM.AddToQueueCommand.CanExecute(null))
        {
            VM.AddToQueueCommand.Execute(null);
            e.Handled = true;
        }
    }
}
