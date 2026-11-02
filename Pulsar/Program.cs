using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Pulsar;

/// <summary>
/// Custom entry point: keeps Pulsar to a single instance. A second launch (for example from Explorer's
/// "Send to" menu or a startup shortcut) forwards its arguments to the running instance and exits.
/// </summary>
public static partial class Program
{
    private const string InstanceKey = "Pulsar.Main";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToRunningInstance())
            return 0;

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool RedirectToRunningInstance()
    {
        AppInstance main;
        try
        {
            main = AppInstance.FindOrRegisterForKey(InstanceKey);
        }
        catch
        {
            return false; // instancing unavailable; just run
        }

        if (main.IsCurrent)
        {
            main.Activated += (_, e) => App.OnRedirectedActivation(e);
            return false;
        }

        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            AllowSetForegroundWindow((int)main.ProcessId); // let the running copy bring itself to the front
            // Redirect on a worker (MTA) thread so this STA thread doesn't deadlock waiting on it.
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // If redirection fails we still exit; the running instance is already there.
        }
        return true;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
