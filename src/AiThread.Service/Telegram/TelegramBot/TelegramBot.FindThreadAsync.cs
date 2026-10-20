namespace AiThread.Service.Telegram;

public sealed partial class TelegramBot
{
    // By the id prefix shown everywhere (8 characters, at least 4), newest first.
    private async Task<Guid?> FindThreadAsync(string prefix, bool activeOnly, CancellationToken cancellationToken)
    {
        var normalized = prefix.Trim();
        if (normalized.Length < 4) return null;

        var recent = await threadService.GetRecentAsync(200, null, cancellationToken);
        return recent.FirstOrDefault(t => (!activeOnly || TelegramMessages.IsActive(t.Status)) &&
                                          t.Id.ToString().StartsWith(normalized, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
