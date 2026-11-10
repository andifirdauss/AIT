using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Data.Messaging;
using AIThread.UI.Hosting;

namespace AIThread.Tests;

public class TrayNotificationTests
{
    private static AppEvent Extracted(int saved, bool failed) => AppEvent.Create(AppEventTypes.MemoryExtracted, new MemoryExtractedDto(saved, failed), Guid.NewGuid());

    [Fact]
    public void The_text_says_how_the_extraction_ended_in_the_language_of_the_ui()
    {
        using (Lang.Use("en"))
        {
            Assert.Equal("1 new memory saved.", TrayNotifications.Text(Extracted(1, false)));
            Assert.Equal("Memory extraction failed.", TrayNotifications.Text(Extracted(0, true)));
        }
        using (Lang.Use("id"))
            Assert.Equal("1 memory baru tersimpan.", TrayNotifications.Text(Extracted(1, false)));
    }

    [Fact]
    public void Nothing_saved_is_not_worth_a_notification() =>
        Assert.Null(TrayNotifications.Text(Extracted(0, false)));

    [Fact]
    public async Task Only_memory_extractions_reach_the_desktop_until_the_tray_closes()
    {
        var bus = new InProcessEventBus();
        var shown = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource();
        var forwarding = TrayNotifications.ForwardAsync(bus, text => shown.TrySetResult(text), stop.Token);

        bus.Publish(AppEvent.Create(AppEventTypes.ThreadUpdated, new { Id = 1 }));
        bus.Publish(Extracted(2, false));

        Assert.Contains("2", await shown.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await stop.CancelAsync();
        await forwarding.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
