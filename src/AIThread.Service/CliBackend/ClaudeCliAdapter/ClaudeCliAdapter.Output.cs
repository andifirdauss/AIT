using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The reply of a finished run, or its failure as a NineRouterException.
    private CliRunOutput Output(string model, ClaudeStreamReader reader, CliExit exit)
    {
        if (reader is { SawResult: true, IsError: false })
        {
            var text = reader.Text;
            if (text.Trim().Length == 0) throw CliFailure.Empty(DisplayName, model);
            return new CliRunOutput(text, reader.FinishReason,
                reader.InputTokens + reader.CacheReadTokens + reader.CacheCreationTokens, reader.OutputTokens, reader.CacheReadTokens);
        }

        var detail = CliProcess.FirstNonEmpty(reader.IsError ? reader.ResultText : null, string.Join(" ", reader.Errors), CliProcess.LastLines(exit.Stderr))
                     ?? Lang.T($"keluar dengan kode {exit.ExitCode}", $"exited with code {exit.ExitCode}");
        logger.LogWarning("Claude Code CLI call to {Model} failed (exit {ExitCode}, subtype {Subtype}, status {Status}): {Detail}",
            CliFailure.Shown(model), exit.ExitCode, reader.ResultSubtype, reader.ApiErrorStatus, detail);
        throw Failure(model, reader.ApiErrorStatus, reader.ResultSubtype, detail);
    }
}
