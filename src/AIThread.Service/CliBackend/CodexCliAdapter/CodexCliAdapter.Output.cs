using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    // The reply of a finished run, or its failure as a NineRouterException.
    private CliRunOutput Output(string model, CodexStreamReader reader, CliExit exit)
    {
        if (reader.CommandCount > 0)
            logger.LogWarning("Codex CLI call to {Model} tried {Count} command(s) despite the shell tool being off.", CliFailure.Shown(model), reader.CommandCount);

        if (reader is { Completed: true, FailedMessage: null } && exit.ExitCode == 0)
        {
            if (reader.Text.Trim().Length == 0) throw CliFailure.Empty(DisplayName, model);
            return new CliRunOutput(reader.Text, "stop", reader.InputTokens, reader.OutputTokens, reader.CachedInputTokens);
        }

        var detail = CliProcess.FirstNonEmpty(reader.FailedMessage, string.Join(" ", reader.Errors), CliProcess.LastLines(exit.Stderr))
                     ?? Lang.T($"keluar dengan kode {exit.ExitCode}", $"exited with code {exit.ExitCode}");
        logger.LogWarning("Codex CLI call to {Model} failed (exit {ExitCode}, status {Status}): {Detail}",
            CliFailure.Shown(model), exit.ExitCode, reader.ApiStatus, detail);
        throw Failure(model, reader.ApiStatus, detail);
    }
}
