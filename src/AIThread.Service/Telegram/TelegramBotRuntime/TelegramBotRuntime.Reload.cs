namespace AIThread.Service.Telegram;

public sealed partial class TelegramBotRuntime
{
    // Interrupts the current long poll; the worker starts over with the saved settings.
    public void Reload() => Interlocked.Exchange(ref _reload, new CancellationTokenSource()).Cancel();
}
