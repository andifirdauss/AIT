using System.ComponentModel;
using System.Diagnostics;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // A restored test project joins the main solution (dotnet sln add, fixed arguments) when the solution does not list
    // it, so the build and the test run find it.
    private async Task AddToSolutionAsync(ProjectContext project, string projectFile, List<string> notes, CancellationToken cancellationToken)
    {
        var solution = project.Modules
            .Where(m => Path.GetExtension(m).ToLowerInvariant() is ".sln" or ".slnx")
            .OrderBy(m => m.Count(c => c == '/'))
            .FirstOrDefault();
        if (solution is null) return;

        var full = Full(project.RootPath, solution);
        try
        {
            if ((await File.ReadAllTextAsync(full, cancellationToken)).Contains(Path.GetFileName(projectFile), StringComparison.OrdinalIgnoreCase)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = project.RootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "sln", full, "add", Full(project.RootPath, projectFile) }) startInfo.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(SolutionTimeout);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.StandardOutput.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            notes.Add(process.ExitCode == 0
                ? $"Project test {projectFile} dipulihkan dan ditambahkan ke {solution}."
                : $"Project test {projectFile} dipulihkan, tetapi belum bisa ditambahkan ke {solution}: {(await error).Trim()}");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not add {Project} to {Solution}.", projectFile, solution);
            notes.Add($"Project test {projectFile} dipulihkan; tambahkan sendiri ke {solution} (dotnet sln add).");
        }
    }
}
