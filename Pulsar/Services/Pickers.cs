using System.Diagnostics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Pulsar.Services;

public static class Pickers
{
    private static nint Owner(nint ownerWindow) => ownerWindow != 0 ? ownerWindow : App.WindowHandle;

    public static async Task<string?> PickFolderAsync(nint ownerWindow = 0)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, Owner(ownerWindow));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public static async Task<string?> PickFileAsync(nint ownerWindow = 0, params string[] extensions)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        if (extensions.Length == 0) picker.FileTypeFilter.Add("*");
        foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
        InitializeWithWindow.Initialize(picker, Owner(ownerWindow));
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<IReadOnlyList<string>> PickFilesAsync(nint ownerWindow = 0)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, Owner(ownerWindow));
        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).ToList() ?? [];
    }

    /// <summary>Opens Explorer with the file selected, or the folder if the file is gone.</summary>
    public static void ShowInExplorer(string? file, string? fallbackFolder)
    {
        try
        {
            if (file is not null && File.Exists(file))
                Process.Start("explorer.exe", $"/select,\"{file}\"");
            else if (file is not null && Directory.Exists(file))
                Process.Start("explorer.exe", $"\"{file}\"");
            else if (fallbackFolder is not null && Directory.Exists(fallbackFolder))
                Process.Start("explorer.exe", $"\"{fallbackFolder}\"");
        }
        catch
        {
            // Explorer unavailable; nothing useful to do.
        }
    }
}
