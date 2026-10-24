using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    // A reply without text and without tool calls is a provider failure, not an answer: callers would otherwise read it
    // as "no edit block", "no summary" or an empty test reply and blame the format.
    private static ChatCompletionResult EnsureReply(string model, ChatCompletionResult result)
    {
        if (result.ToolCalls.Count > 0 || !string.IsNullOrWhiteSpace(result.Content)) return result;

        var reason = result.FinishReason == "length"
            ? "batas max tokens habis sebelum ada teks, mungkin terpakai untuk reasoning"
            : "tanpa teks dan tanpa tool call";
        throw new NineRouterException(
            $"Model '{model}' mengembalikan jawaban kosong lewat 9router ({reason}). Cek provider model ini di 9router, atau pilih model lain / node cadangan.",
            502)
        {
            IsEmptyReply = true
        };
    }
}
