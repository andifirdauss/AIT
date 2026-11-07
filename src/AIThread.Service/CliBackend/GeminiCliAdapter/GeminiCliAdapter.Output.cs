using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    // The reply of a finished run, or its failure as a NineRouterException.
    private CliRunOutput Output(string model, GeminiStreamReader reader, CliExit exit)
    {
        if (reader.ToolUseCount > 0)
            logger.LogWarning("Gemini CLI call to {Model} tried {Count} tool(s) despite the tools being denied.", CliFailure.Shown(model), reader.ToolUseCount);

        if (reader is { SawResult: true, IsError: false } && exit.ExitCode == 0)
        {
            if (reader.Text.Trim().Length == 0) throw CliFailure.Empty(DisplayName, model);
            return new CliRunOutput(reader.Text, "stop", reader.InputTokens, reader.OutputTokens, reader.CachedTokens);
        }

        var detail = CliProcess.FirstNonEmpty(reader.ErrorMessage, string.Join(" ", reader.Errors), CliProcess.LastLines(exit.Stderr))
                     ?? Lang.T($"keluar dengan kode {exit.ExitCode}", $"exited with code {exit.ExitCode}");
        var status = CliFailure.StatusIn(detail);
        logger.LogWarning("Gemini CLI call to {Model} failed (exit {ExitCode}, error {ErrorType}, status {Status}): {Detail}",
            CliFailure.Shown(model), exit.ExitCode, reader.ErrorType, status, detail);
        throw Failure(model, status, detail,
            exit.ExitCode == AuthExitCode || reader.ErrorType?.Contains("Auth", StringComparison.OrdinalIgnoreCase) == true);
    }
}
