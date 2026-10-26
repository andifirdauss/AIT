using AIThread.Common.Localization;
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
            ? Lang.T("batas max tokens habis sebelum ada teks, mungkin terpakai untuk reasoning", "the max tokens limit ran out before any text, probably used for reasoning")
            : Lang.T("tanpa teks dan tanpa tool call", "no text and no tool call");
        throw new NineRouterException(
            Lang.T($"Model '{model}' mengembalikan jawaban kosong lewat 9router ({reason}). Cek provider model ini di 9router, atau pilih model lain / node cadangan.", $"Model '{model}' returned an empty answer through 9router ({reason}). Check this model's provider in 9router, or choose another model / fallback node."),
            502)
        {
            IsEmptyReply = true
        };
    }
}
