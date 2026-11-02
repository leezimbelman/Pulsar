using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pulsar.Models;
using Pulsar.ViewModels;

namespace Pulsar.Pages;

public sealed partial class SubscriptionsPage : Page
{
    public SubscriptionsViewModel VM => App.Subscriptions;

    public SubscriptionsPage()
    {
        InitializeComponent();
    }

    private static Subscription SubOf(object sender) =>
        ((FrameworkElement)sender).Tag as Subscription ?? (Subscription)((FrameworkElement)sender).DataContext;

    private void Check_Click(object sender, RoutedEventArgs e)
    {
        VM.Check(SubOf(sender));
        App.ShowMainWindow("download"); // the check shows up in the download queue
    }

    private void Folder_Click(object sender, RoutedEventArgs e) => SubscriptionsViewModel.OpenFolder(SubOf(sender));
    private void Remove_Click(object sender, RoutedEventArgs e) => VM.Remove(SubOf(sender));

    private void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch { DataContext: Subscription sub } toggle && toggle.IsOn != sub.Enabled)
            VM.SetEnabled(sub, toggle.IsOn);
    }
}
