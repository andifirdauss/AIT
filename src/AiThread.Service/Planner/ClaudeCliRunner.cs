using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Planner;

// The CLI could not start, failed, hit its limits or timed out. Message is user-facing.
public sealed class ClaudeCliException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed record ClaudeCliRun(string Model, string Prompt, string WorkingDirectory, IReadOnlyList<string> Arguments, TimeSpan Timeout);

public sealed record ClaudeCliToolUse(string Name, JsonObject Input);

public sealed record ClaudeCliResult(string Text, bool IsError, string? Subtype, int Turns, int Tokens);

public sealed record ClaudeCliPing(bool Success, string? Reply, int Tokens, string? Error);

// Runs `claude -p` headless: prompt on stdin, newline-delimited JSON events (--output-format stream-json) on stdout.
// Each child gets its own session id, registered so the proxy can refuse requests that loop back into AiThread.
public sealed class ClaudeCliRunner(
    IOptionsMonitor<AiThreadOptions> options,
    PlannerSessionRegistry sessions,
    ILogger<ClaudeCliRunner> logger)
{
    private const int MaxErrorChars = 1500;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Would point the child at this proxy, or hand it the proxy key, if AiThread itself was started with them.
    private static readonly string[] ScrubbedVariables = ["ANTHROPIC_BASE_URL", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_CUSTOM_HEADERS"];

    public async Task<ClaudeCliResult> RunAsync(ClaudeCliRun run, Func<ClaudeCliToolUse, Task>? onToolUse, CancellationToken cancellationToken)
    {
        var cli = options.CurrentValue.ClaudeCli;
        var sessionId = Guid.NewGuid().ToString();
        var arguments = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--model", run.Model, "--session-id", sessionId };
        if (!string.IsNullOrWhiteSpace(cli.SettingSources)) arguments.AddRange(["--setting-sources", cli.SettingSources.Trim()]);
        arguments.AddRange(run.Arguments);
        arguments.AddRange(cli.ExtraArguments.Where(a => !string.IsNullOrWhiteSpace(a)));

        using var registration = sessions.RegisterCliSession(sessionId);
        using var process = new Process { StartInfo = CreateStartInfo(cli.Command, arguments, run.WorkingDirectory) };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new ClaudeCliException(
                $"Claude Code CLI tidak dapat dijalankan ('{cli.Command}'): {ex.Message}. Pastikan CLI terpasang dan sudah login, " +
                "atau isi AiThread:ClaudeCli:Command di appsettings dengan path lengkap.", ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(run.Timeout);
        var stderr = ReadAllSafeAsync(process.StandardError);

        JsonObject? result = null;
        try
        {
            await process.StandardInput.WriteAsync(run.Prompt.AsMemory(), timeout.Token);
            process.StandardInput.Close();

            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (ParseEvent(line) is not { } evt) continue;
                switch (Str(evt["type"]))
                {
                    case "assistant" when onToolUse is not null:
                        foreach (var use in ToolUses(evt)) await onToolUse(use);
                        break;
                    case "result":
                        result = evt;
                        break;
                }
            }

            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ClaudeCliException($"Claude Code CLI tidak selesai dalam {run.Timeout.TotalMinutes:0} menit.");
        }
        finally
        {
            Kill(process);
        }

        if (result is null)
        {
            var error = Limit((await stderr).Trim());
            throw new ClaudeCliException(
                $"Claude Code CLI berhenti (exit {process.ExitCode}) tanpa hasil" + (error.Length > 0 ? $": {error}" : "."));
        }

        var subtype = Str(result["subtype"]);
        var isError = result["is_error"] is JsonValue flag && flag.TryGetValue<bool>(out var e) && e
                      || subtype?.StartsWith("error", StringComparison.OrdinalIgnoreCase) == true;
        var usage = result["usage"];
        var tokens = Int(usage?["input_tokens"]) + Int(usage?["cache_creation_input_tokens"])
                     + Int(usage?["cache_read_input_tokens"]) + Int(usage?["output_tokens"]);

        return new ClaudeCliResult(Str(result["result"]) ?? string.Empty, isError, subtype, Int(result["num_turns"]), tokens);
    }

    // Cheap end-to-end check for the node Test button: installed, logged in, model name accepted.
    public async Task<ClaudeCliPing> PingAsync(string model, CancellationToken cancellationToken)
    {
        try
        {
            var result = await RunAsync(
                new ClaudeCliRun(model, "Reply with exactly one word: pong", Path.GetTempPath(), ["--max-turns", "1"], TimeSpan.FromMinutes(2)),
                null,
                cancellationToken);
            return result.IsError
                ? new ClaudeCliPing(false, null, result.Tokens, Limit(result.Text.Length > 0 ? result.Text : result.Subtype ?? "error"))
                : new ClaudeCliPing(true, result.Text.Trim(), result.Tokens, null);
        }
        catch (ClaudeCliException ex)
        {
            return new ClaudeCliPing(false, null, 0, ex.Message);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string command, IReadOnlyList<string> arguments, string workingDirectory)
    {
        var executable = ResolveExecutable(command);
        var info = new ProcessStartInfo
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8
        };

        var extension = Path.GetExtension(executable);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // npm installs a .cmd shim, which only cmd.exe can run. /s strips the outer quotes and keeps the rest verbatim.
            info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            info.Arguments = "/d /s /c \"" + string.Join(' ', new[] { executable }.Concat(arguments).Select(QuoteForCmd)) + "\"";
        }
        else
        {
            info.FileName = executable;
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
        }

        foreach (var name in ScrubbedVariables) info.Environment.Remove(name);
        info.Environment["DISABLE_AUTOUPDATER"] = "1";
        return info;
    }

    // Process.Start does not apply PATHEXT, so "claude" must be resolved to claude.exe / claude.cmd here.
    internal static string ResolveExecutable(string command)
    {
        command = command.Trim().Trim('"');
        if (Path.IsPathRooted(command) || command.Contains('/') || command.Contains('\\')) return command;

        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"'))
            // The native installer puts claude in ~/.local/bin, which a service or IDE-launched process may not have on PATH.
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));

        foreach (var directory in directories)
        foreach (var extension in extensions)
        {
            var candidate = Path.Combine(directory, command + extension);
            if (File.Exists(candidate)) return candidate;
        }

        return command;
    }

    internal static string QuoteForCmd(string argument)
    {
        if (argument.Contains('"')) throw new ClaudeCliException($"Argumen CLI tidak boleh mengandung tanda kutip: {argument}");
        return argument.Length == 0 || argument.IndexOfAny([' ', '\t', '&', '|', '<', '>', '^', '(', ')', ',', ';', '=', '%', '!']) >= 0
            ? $"\"{argument}\""
            : argument;
    }

    private static IEnumerable<ClaudeCliToolUse> ToolUses(JsonObject evt)
    {
        if (evt["message"]?["content"] is not JsonArray content) yield break;
        foreach (var block in content)
        {
            if (block is JsonObject obj && Str(obj["type"]) == "tool_use" && Str(obj["name"]) is { Length: > 0 } name)
                yield return new ClaudeCliToolUse(name, obj["input"] as JsonObject ?? []);
        }
    }

    private void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            logger.LogDebug(ex, "Claude Code CLI process could not be killed; it probably exited already.");
        }
    }

    private static async Task<string> ReadAllSafeAsync(StreamReader reader)
    {
        try
        {
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            return string.Empty;
        }
    }

    private static JsonObject? ParseEvent(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return null;
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Limit(string text) => text.Length <= MaxErrorChars ? text : text[..MaxErrorChars] + "…";

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int Int(JsonNode? node)
    {
        if (node is not JsonValue value) return 0;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return (int)Math.Clamp(l, 0, int.MaxValue);
        return value.TryGetValue<double>(out var d) ? (int)d : 0;
    }
}
