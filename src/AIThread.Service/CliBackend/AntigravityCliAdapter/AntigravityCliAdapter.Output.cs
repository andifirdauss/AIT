using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // The reply of a finished run, or its failure as a NineRouterException.
    private CliRunOutput Output(string model, AntigravityStreamReader reader, CliExit exit)
    {
        if (reader.ToolUseCount > 0)
            logger.LogWarning("Antigravity CLI call to {Model} tried {Count} tool(s) despite the agent having none.", CliFailure.Shown(model), reader.ToolUseCount);

        if (reader is { SawResult: true, IsError: false } && exit.ExitCode == 0)
        {
            if (reader.Text.Trim().Length == 0) throw CliFailure.Empty(DisplayName, model);
            var finish = reader.FinishedReason is "max_tokens" or "length" ? "length" : "stop";
            return new CliRunOutput(reader.Text.TrimEnd('\n'), finish, reader.InputTokens, reader.OutputTokens, reader.CacheReadTokens);
        }

        var detail = CliProcess.FirstNonEmpty(reader.Error, CliProcess.LastLines(exit.Stderr))
                     ?? Lang.T($"keluar dengan kode {exit.ExitCode}", $"exited with code {exit.ExitCode}");
        var status = CliFailure.StatusIn(detail);
        logger.LogWarning("Antigravity CLI call to {Model} failed (exit {ExitCode}, finished {Finished}, status {Status}): {Detail}",
            CliFailure.Shown(model), exit.ExitCode, reader.FinishedReason, status, detail);
        throw Failure(model, status, detail);
    }
}
