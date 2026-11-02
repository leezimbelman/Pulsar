using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pulsar.Models;
using Pulsar.ViewModels;

namespace Pulsar.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel VM => App.HistoryVM;

    public HistoryPage()
    {
        InitializeComponent();
    }

    private static HistoryEntry EntryOf(object sender) =>
        ((FrameworkElement)sender).Tag as HistoryEntry ?? (HistoryEntry)((FrameworkElement)sender).DataContext;

    private void Play_Click(object sender, RoutedEventArgs e) => HistoryViewModel.Play(EntryOf(sender));
    private void Folder_Click(object sender, RoutedEventArgs e) => HistoryViewModel.ShowInFolder(EntryOf(sender));
    private void Again_Click(object sender, RoutedEventArgs e) => HistoryViewModel.DownloadAgain(EntryOf(sender));
    private void Copy_Click(object sender, RoutedEventArgs e) => HistoryViewModel.CopyLink(EntryOf(sender));
    private void Remove_Click(object sender, RoutedEventArgs e) => VM.Remove(EntryOf(sender));

    private void Tools_Click(object sender, RoutedEventArgs e)
    {
        App.Tools.AddFiles([EntryOf(sender).FilePath]);
        App.ShowMainWindow("tools");
    }
}
