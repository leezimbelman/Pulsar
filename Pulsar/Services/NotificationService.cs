using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Pulsar.Services;

/// <summary>Windows toast notifications ("Download finished", with Play / Show in folder buttons).</summary>
public static class NotificationService
{
    private static bool _registered;

    public static void Initialize()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch
        {
            _registered = false; // e.g. running elevated; notifications just won't show
        }
    }

    public static void Shutdown()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { /* exiting */ }
    }

    public static void Show(string title, string body, string? filePath = null)
    {
        if (!_registered) return;
        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", "show")
                .AddText(title)
                .AddText(body);

            if (filePath is not null && File.Exists(filePath))
            {
                builder.AddButton(new AppNotificationButton("Play").AddArgument("action", "play").AddArgument("path", filePath));
                builder.AddButton(new AppNotificationButton("Show in folder").AddArgument("action", "folder").AddArgument("path", filePath));
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch
        {
            // Notifications are a nice-to-have.
        }
    }

    private static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var arguments = new Dictionary<string, string>(args.Arguments);
        App.Dispatch(() => App.HandleNotification(arguments));
    }
}
