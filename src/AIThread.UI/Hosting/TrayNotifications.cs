using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Data.Messaging;

namespace AIThread.UI.Hosting;

// What the tray icon tells the developer on the Windows desktop: the answer in the CLI has ended by the time these happen.
public static class TrayNotifications
{
    // Null for an event that is not worth a notification.
    public static string? Text(AppEvent evt) => evt.Data switch
    {
        MemoryExtractedDto { Failed: true } => Lang.T("Ekstraksi memory gagal.", "Memory extraction failed."),
        MemoryExtractedDto { Saved: > 0 } memory => Lang.T($"{memory.Saved} memory baru tersimpan.", $"{memory.Saved} new memory saved."),
        _ => null
    };

    // Until the token is cancelled.
    public static async Task ForwardAsync(IEventBus events, Action<string> show, CancellationToken cancellationToken)
    {
        using var subscription = events.Subscribe(e => e.Type == AppEventTypes.MemoryExtracted);
        try
        {
            await foreach (var evt in subscription.Reader.ReadAllAsync(cancellationToken))
                if (Text(evt) is { } text) show(text);
        }
        catch (OperationCanceledException)
        {
            // The tray is closing.
        }
    }
}
