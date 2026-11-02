using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Pulsar.ViewModels;

namespace Pulsar.Pages;

public sealed partial class ToolsPage : Page
{
    public ToolsViewModel VM => App.Tools;

    public ToolsPage()
    {
        InitializeComponent();
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to the list";
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        VM.AddFiles(items.OfType<StorageFile>().Select(f => f.Path));
    }
}
