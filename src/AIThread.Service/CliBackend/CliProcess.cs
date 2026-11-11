using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.CliBackend;

// How to start a CLI: the executable and the arguments before AIT's own (the package's JS entry for an npm install
// run by node).
public sealed record CliLaunch(string FileName, IReadOnlyList<string> Prefix)
{
    public string Display => Prefix.Count == 0 ? FileName : Prefix[^1];
}

// How one CLI process ended: its exit code and the end of stderr.
public sealed record CliExit(int ExitCode, string Stderr);

// What the CLI adapters share: finding the binary (PATH, install folders, the npm shim resolved to the package's own
// entry), `--version`, one headless run with a stdin input and line-by-line stdout under a timeout and a stall limit,
// and the empty working folder of each call.
public static class CliProcess
{
    public static readonly UTF8Encoding Utf8 = new(false);

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

    // How long a stopped CLI gets to go, so its working folder can be deleted.
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(2);

    // A fresh, empty folder per call: nothing of the project (no CLAUDE.md, AGENTS.md, GEMINI.md, settings) for the
    // CLI to read.
    public static readonly string WorkRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "cli-work");

    public static string NewWorkFolder()
    {
        var folder = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    // The configured path, else <command> on PATH or in one of the extra folders, else (Windows) the npm shim
    // <command>.cmd, resolved to the package's entry: a .cmd would need cmd.exe and its quoting.
    public static CliLaunch? Resolve(string? configured, string command, string npmPackage, IEnumerable<string>? extraFolders = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return FromFile(Environment.ExpandEnvironmentVariables(configured.Trim().Trim('"')), npmPackage, command);

        var folders = extraFolders?.ToList() ?? [];
        if (!OperatingSystem.IsWindows())
        {
            var unix = FindOnPath(command) ?? folders.Select(f => Existing(Path.Combine(f, command))).FirstOrDefault(p => p is not null);
            return unix is null ? null : new CliLaunch(unix, []);
        }

        var exe = FindOnPath(command + ".exe") ?? folders.Select(f => Existing(Path.Combine(f, command + ".exe"))).FirstOrDefault(p => p is not null);
        if (exe is not null) return new CliLaunch(exe, []);

        var shim = FindOnPath(command + ".cmd")
                   ?? Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", command + ".cmd"));
        return shim is null ? null : FromNpmShim(shim, npmPackage, command);
    }

    // A configured path: an executable, the package's JS entry (run by node) or the npm shim.
    public static CliLaunch? FromFile(string path, string npmPackage, string command)
    {
        if (!File.Exists(path)) return null;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".js" or ".mjs" or ".cjs" => FindOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node") is { } node ? new CliLaunch(node, [path]) : null,
            ".cmd" or ".bat" => FromNpmShim(path, npmPackage, command),
            _ => new CliLaunch(path, [])
        };
    }

    // <npm prefix>\<command>.cmd → <npm prefix>\node_modules\<package>, entry from package.json "bin".
    public static CliLaunch? FromNpmShim(string shim, string npmPackage, string command)
    {
        var folder = Path.GetDirectoryName(shim);
        if (folder is null) return null;
        var package = Path.Combine([folder, "node_modules", .. npmPackage.Split('/')]);
        var manifest = Path.Combine(package, "package.json");
        if (!File.Exists(manifest)) return null;

        string? entry;
        try
        {
            var bin = JsonNode.Parse(File.ReadAllText(manifest))?["bin"];
            entry = bin is JsonObject map ? map[command]?.GetValue<string>() : bin?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(entry)) return null;

        var target = Path.GetFullPath(Path.Combine(package, entry));
        if (!File.Exists(target)) return null;
        if (!IsScript(target)) return new CliLaunch(target, []);

        var node = Existing(Path.Combine(folder, "node.exe")) ?? FindOnPath("node.exe");
        return node is null ? null : new CliLaunch(node, [target]);
    }

    // `<cli> --version` only: whether it is logged in shows on the first call (the node test), not here, since asking
    // would spend a request on the account.
    public static async Task<CliBackendStatusDto> ProbeAsync(string tool, string name, string command, CliLaunch? launch,
        Action<IDictionary<string, string?>> cleanEnvironment, ILogger logger, CancellationToken cancellationToken)
    {
        if (launch is null) return new CliBackendStatusDto(tool, name, false, null, null, null);

        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8
        };
        foreach (var argument in launch.Prefix) startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("--version");
        cleanEnvironment(startInfo.Environment);

        using var process = new Process { StartInfo = startInfo };
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(VersionTimeout);
        try
        {
            process.Start();
            var stderr = process.StandardError.ReadToEndAsync(limit.Token);
            var output = await process.StandardOutput.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            await stderr;
            var version = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return process.ExitCode == 0
                ? new CliBackendStatusDto(tool, name, true, launch.Display, version, null)
                : new CliBackendStatusDto(tool, name, true, launch.Display, null,
                    Lang.T($"`{command} --version` keluar dengan kode {process.ExitCode}.", $"`{command} --version` exited with code {process.ExitCode}."));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            return new CliBackendStatusDto(tool, name, true, launch.Display, null,
                Lang.T($"`{command} --version` tidak menjawab.", $"`{command} --version` did not answer."));
        }
        catch (Win32Exception ex)
        {
            logger.LogWarning(ex, "Could not start {Cli} at {Path}.", name, launch.Display);
            return new CliBackendStatusDto(tool, name, false, launch.Display, null, ex.Message);
        }
    }

    // One headless run in the working folder: the input goes on stdin (no command-line length limit) and is closed,
    // every stdout line goes to onLine. Dropped after limits.Timeout in total or limits.Stall without a line.
    // finished: true once the answer is complete, so a CLI that takes its time to shut down is not waited for.
    public static Task<CliExit> RunAsync(CliLaunch launch, IEnumerable<string> arguments, string workFolder, string? input,
        Action<IDictionary<string, string?>> cleanEnvironment, Func<string, Task> onLine, CliCallLimits limits, string name,
        Func<Exception, NineRouterException> notInstalled, CancellationToken cancellationToken, Func<bool>? finished = null) =>
        RunStartedAsync(Start(launch, arguments, workFolder, cleanEnvironment, notInstalled), input, onLine, limits, name, cancellationToken, finished);

    // Starts the CLI, which then waits for its input on stdin.
    public static Process Start(CliLaunch launch, IEnumerable<string> arguments, string workFolder,
        Action<IDictionary<string, string?>> cleanEnvironment, Func<Exception, NineRouterException> notInstalled)
    {
        var startInfo = new ProcessStartInfo(launch.FileName)
        {
            WorkingDirectory = workFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };
        foreach (var argument in launch.Prefix.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        cleanEnvironment(startInfo.Environment);
        // A node's own CLI must not be sent back to AIT by the plugin hook of the developer's CLI session.
        startInfo.Environment["AITHREAD_CLI_ENGINE"] = "1";

        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            return process;
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw notInstalled(ex);
        }
    }

    // The run of a CLI already started (RunAsync, or one waiting in a CliWarmPool). The process ends with it.
    public static async Task<CliExit> RunStartedAsync(Process started, string? input, Func<string, Task> onLine, CliCallLimits limits, string name,
        CancellationToken cancellationToken, Func<bool>? finished = null)
    {
        using var process = started;
        using var total = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        total.CancelAfter(limits.Timeout);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(total.Token);
        try
        {
            try
            {
                if (input is not null) await process.StandardInput.WriteAsync(input.AsMemory(), total.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The CLI exited before reading its input; its output says why.
            }

            var stderr = process.StandardError.ReadToEndAsync(total.Token);
            while (true)
            {
                stall.CancelAfter(limits.Stall);
                var line = await process.StandardOutput.ReadLineAsync(stall.Token);
                if (line is null) break;
                await onLine(line);
                if (finished?.Invoke() != true) continue;

                // Gone before the caller deletes the working folder the process runs in.
                Kill(process);
                using var exit = new CancellationTokenSource(ExitWait);
                try
                {
                    await process.WaitForExitAsync(exit.Token);
                }
                catch (OperationCanceledException)
                {
                    // Still going down; the folder is cleaned up on a best-effort basis anyway.
                }
                return new CliExit(0, string.Empty);
            }
            await process.WaitForExitAsync(total.Token);
            return new CliExit(process.ExitCode, await stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (total.IsCancellationRequested)
            {
                var limit = (int)limits.Timeout.TotalSeconds;
                throw new NineRouterException(
                    Lang.T($"{name} CLI belum selesai menjawab dalam {limit} detik. Naikkan Setting → Timeout (detik) bila jawaban model memang panjang.",
                        $"{name} CLI did not finish its answer within {limit} seconds. Raise Setting → Timeout (seconds) when the model's answer is really long."), 408)
                {
                    // Under a shorter limit than the stall check, a silent CLI ends here too: that one may retry.
                    IsTimeLimit = limits.Timeout > limits.Stall
                };
            }

            var silent = (int)limits.Stall.TotalSeconds;
            throw new NineRouterException(
                Lang.T($"{name} CLI tidak mengirim output selama {silent} detik.", $"{name} CLI sent no output for {silent} seconds."), 408);
        }
        finally
        {
            Kill(process);
        }
    }

    // A warm process nobody will use: stopped and its folder removed.
    public static void Discard(WarmCliProcess warm)
    {
        Kill(warm.Process);
        try
        {
            warm.Process.WaitForExit(ExitWait);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
        warm.Process.Dispose();
        DeleteQuietly(warm.WorkFolder);
    }

    public static string? FindOnPath(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry.
            }
        }
        return null;
    }

    public static string? Existing(string path) => File.Exists(path) ? path : null;

    public static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held by the exiting process. It only holds prompts already masked by SecretGuard.
        }
    }

    // The end of stderr, where a CLI prints why it stopped.
    public static string? LastLines(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed.Length <= 600 ? trimmed : "…" + trimmed[^600..];
    }

    public static bool IsLoopback(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    public static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    // The user turn for a CLI that takes text only: attached images are named in a note instead of being dropped
    // silently.
    public static string WithImageNote(string text, int images) =>
        images == 0
            ? text
            : $"{text}\n\n(Note from AIT: {images} image(s) were attached to this message but cannot be passed to this model. Say so if the answer depends on them.)";

    // An image of the prompt written as a file in the working folder, for a CLI that takes image paths.
    public static string WriteImage(string workFolder, CliImage image, int index)
    {
        var extension = image.MediaType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            _ => ".png"
        };
        var path = Path.Combine(workFolder, $"image-{index + 1}{extension}");
        File.WriteAllBytes(path, Convert.FromBase64String(image.Data));
        return path;
    }

    private static bool IsScript(string path) =>
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase)
                                                                 || path.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase);

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited, or never started.
        }
    }
}
