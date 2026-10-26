using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Workspace;

namespace AIThread.Service.Verification;

// How a build result is shown: Format for models (English, paths as the planner sees them), Headline for people.
public static class BuildReport
{
    private const int MaxTailChars = 4000;

    public static string Format(BuildResult result, WorkspaceScope scope, int maxErrors = 40)
    {
        var builder = new StringBuilder();
        var seconds = $"{result.DurationMs / 1000.0:0.0}s";

        if (result.StartError is not null)
            return $"Build `{result.Command}` could not start: {result.StartError}\n";

        if (result.TimedOut)
            builder.Append($"Build `{result.Command}` timed out after {seconds} and was stopped.\n");
        else if (result.Succeeded)
            builder.Append($"Build `{result.Command}` succeeded in {seconds}" + (result.WarningCount > 0 ? $" ({result.WarningCount} warnings)." : ".") + "\n");
        else if (result.OnlyLockedOutput)
            builder.Append($"Build `{result.Command}` compiled, but copying the output failed because a running program locks the files ({seconds}). This is not a code problem.\n");
        else
            builder.Append($"Build `{result.Command}` FAILED in {seconds} (exit code {result.ExitCode}): {result.Errors.Count} error(s), {result.WarningCount} warning(s).\n");

        if (result.InUseOutput is { } inUse)
            builder.Append($"It ran without a clean because a running program uses `{scope.ToDisplay(inUse)}`.\n");

        foreach (var error in result.Errors.Take(maxErrors)) builder.Append("- ").Append(Line(error, scope)).Append('\n');
        if (result.Errors.Count > maxErrors) builder.Append($"- ... {result.Errors.Count - maxErrors} more error(s)\n");

        // Without recognised errors the raw output is the only clue.
        if (!result.Succeeded && result.Errors.Count == 0 && !string.IsNullOrWhiteSpace(result.OutputTail))
        {
            var tail = result.OutputTail.Length <= MaxTailChars ? result.OutputTail : "..." + result.OutputTail[^MaxTailChars..];
            builder.Append("Last output lines:\n```\n").Append(tail.TrimEnd()).Append("\n```\n");
        }

        return builder.ToString();
    }

    public static string Line(BuildDiagnostic error, WorkspaceScope scope)
    {
        var location = error.File is null ? string.Empty : scope.ToDisplay(error.File);
        if (error.Line is { } line) location += error.Column is { } column ? $"({line},{column})" : $"({line})";
        var code = error.Code is null ? string.Empty : $"{error.Code}: ";
        return location.Length == 0 ? $"{code}{error.Message}" : $"{location}: {code}{error.Message}";
    }

    // One line for progress output and the activity log (in the UI language).
    public static string Headline(BuildResult result)
    {
        var headline = HeadlineCore(result);
        return result.InUseOutput is { } inUse && result.StartError is null && !result.OnlyLockedOutput
            ? Lang.T($"{headline}; tanpa clean karena {Path.GetFileName(inUse)} sedang dipakai program yang berjalan", $"{headline}; without clean because {Path.GetFileName(inUse)} is in use by a running program")
            : headline;
    }

    private static string HeadlineCore(BuildResult result)
    {
        var seconds = $"{result.DurationMs / 1000.0:0.0}s";
        if (result.StartError is not null) return Lang.T($"build tidak bisa dijalankan — {result.StartError}", $"the build cannot run — {result.StartError}");
        if (result.TimedOut) return Lang.T($"build melewati batas waktu ({seconds}) dan dihentikan", $"the build exceeded the time limit ({seconds}) and was stopped");
        if (result.Succeeded) return Lang.T($"build berhasil ({seconds}{(result.WarningCount > 0 ? $", {result.WarningCount} warning" : string.Empty)})", $"build succeeded ({seconds}{(result.WarningCount > 0 ? $", {result.WarningCount} warnings" : string.Empty)})");
        if (result.OnlyLockedOutput) return Lang.T($"kompilasi berhasil, tetapi file output terkunci oleh program yang sedang berjalan ({seconds})", $"compilation succeeded, but an output file is locked by a running program ({seconds})");
        return Lang.T($"build gagal: {result.Errors.Count} error ({seconds})", $"build failed: {result.Errors.Count} errors ({seconds})");
    }
}
