using System.Text.Json;

namespace AIThread.Service.Telegram;

public sealed partial class TelegramThreadReporter
{
    // thread.output events carry { kind = "planner.tool", tool, detail }.
    private static string? DescribePlannerActivity(object? data)
    {
        if (data is null) return null;
        try
        {
            var json = JsonSerializer.SerializeToElement(data);
            if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("tool", out var tool)) return null;
            var detail = json.TryGetProperty("detail", out var value) ? value.GetString() : null;
            return string.IsNullOrWhiteSpace(detail) ? tool.GetString() : $"{tool.GetString()} · {detail}";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
