using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pulsar.Services;

/// <summary>Explorer "Send to" shortcuts and "start with Windows".</summary>
public static partial class ShellIntegration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Pulsar";

    private static string Exe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pulsar.exe");

    private static string SendToFolder => Environment.GetFolderPath(Environment.SpecialFolder.SendTo);
    private static string PlayShortcut => Path.Combine(SendToFolder, "Pulsar Player.lnk");
    private static string ToolsShortcut => Path.Combine(SendToFolder, "Pulsar Tools.lnk");

    public static bool IsSendToInstalled => File.Exists(PlayShortcut) || File.Exists(ToolsShortcut);

    public static void InstallSendTo()
    {
        CreateShortcut(PlayShortcut, "--play", "Play in Pulsar");
        CreateShortcut(ToolsShortcut, "--tools", "Open in Pulsar's tools (convert, trim, transcribe…)");
    }

    public static void RemoveSendTo()
    {
        foreach (var path in new[] { PlayShortcut, ToolsShortcut })
            if (File.Exists(path)) File.Delete(path);
    }

    private static void CreateShortcut(string path, string arguments, string description)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host isn't available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = Exe;
            link.Arguments = arguments;
            link.Description = description;
            link.IconLocation = Exe + ",0";
            link.WorkingDirectory = Path.GetDirectoryName(Exe);
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Exe}\" --tray");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    /// <summary>Splits a Windows command line the same way the C runtime does.</summary>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        nint argv = CommandLineToArgvW(commandLine, out int count);
        if (argv == 0) return [];
        try
        {
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
                result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "");
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CommandLineToArgvW(string commandLine, out int count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint mem);
}
