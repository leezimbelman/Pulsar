using Microsoft.UI.Xaml.Controls;
using Pulsar.ViewModels;

namespace Pulsar.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel VM => App.SettingsVM;

    public SettingsPage()
    {
        InitializeComponent();
    }
}
