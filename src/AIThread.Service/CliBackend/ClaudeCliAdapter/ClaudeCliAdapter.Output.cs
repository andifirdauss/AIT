using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The reply of a finished run, or its failure as a NineRouterException.
    private CliRunOutput Output(string model, ClaudeStreamReader reader, int exitCode, string stderr)
    {
        if (reader is { SawResult: true, IsError: false })
        {
            var text = reader.Text;
            if (text.Trim().Length == 0)
            {
                throw new NineRouterException(
                    Lang.T($"Model '{Shown(model)}' lewat Claude Code CLI mengembalikan jawaban kosong.", $"Model '{Shown(model)}' through Claude Code CLI returned an empty answer."), 502)
                {
                    IsEmptyReply = true
                };
            }
            return new CliRunOutput(text, reader.FinishReason,
                reader.InputTokens + reader.CacheReadTokens + reader.CacheCreationTokens, reader.OutputTokens, reader.CacheReadTokens);
        }

        var detail = FirstNonEmpty(reader.IsError ? reader.ResultText : null, string.Join(" ", reader.Errors), LastLines(stderr))
                     ?? Lang.T($"keluar dengan kode {exitCode}", $"exited with code {exitCode}");
        logger.LogWarning("Claude Code CLI call to {Model} failed (exit {ExitCode}, subtype {Subtype}, status {Status}): {Detail}",
            Shown(model), exitCode, reader.ResultSubtype, reader.ApiErrorStatus, detail);
        throw Failure(model, reader.ApiErrorStatus, reader.ResultSubtype, detail);
    }
}
