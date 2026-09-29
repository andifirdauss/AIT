using System.Globalization;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Options;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Planner;

// Text is the CLI's final message, used when it ended without calling submit_plan.
public sealed record CliPlanningResult(ThreadPlan? Plan, string Text, int Turns, int Tokens);

// Plans with the vendor's Claude Code CLI instead of a 9router chat loop. The CLI explores the repository with its own
// read-only tools (Read, Grep, Glob) and reaches AiThread's memory and submit_plan through an MCP server hosted by
// AiThread itself (McpPlannerController). Only read tools exist in the session, so the CLI cannot change the project.
public sealed class ClaudeCliPlanner(
    ClaudeCliRunner runner,
    PlannerSessionRegistry sessions,
    LocalServerUrl localUrl,
    IOptionsMonitor<AiThreadOptions> options)
{
    public const string McpServerName = "aithread";

    // After a plan is accepted the CLI normally ends its turn at once; stop it if it keeps going.
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);

    private static readonly string[] McpTools = [PlannerTools.SearchMemory, PlannerTools.ReadMemory, PlannerTools.ReadThread, PlannerTools.SubmitPlan];
    private static readonly string[] ReadOnlyTools = ["Read", "Grep", "Glob"];

    public const string ToolNotes = """
        Tools in this environment
        - Investigate the repository with the built-in Read, Grep and Glob tools; they replace list_directory, find_files, search_text and read_file mentioned above. The working directory is the project root. Read needs absolute paths: join the root shown in the brief with the relative path.
        - search_memory, read_memory, read_thread and submit_plan come from the "aithread" MCP server.
        - You cannot edit files or run commands. Every path inside submit_plan must be relative to the project root and use forward slashes.
        - Your work ends with the submit_plan call. When it answers "Plan accepted.", stop immediately without further output.
        """;

    public async Task<CliPlanningResult> PlanAsync(
        AiNode node,
        string prompt,
        string workingDirectory,
        int maxTurns,
        Func<ToolCall, CancellationToken, Task<PlannerToolOutcome>> runTool,
        Func<ClaudeCliToolUse, Task> onToolUse,
        CancellationToken cancellationToken)
    {
        var baseUrl = localUrl.BaseUrl
                      ?? throw new ClaudeCliException("Alamat lokal AiThread belum diketahui untuk server MCP planner. Isi AiThread:LocalBaseUrl di appsettings.");

        ThreadPlan? accepted = null;
        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var session = sessions.Open(ToolDefinitions(), async (name, arguments, token) =>
        {
            // Only the listed tools; repository access goes through the CLI's own read-only tools.
            if (!McpTools.Contains(name)) return new McpToolResult($"Error: unknown tool '{name}'.", true);

            var outcome = await runTool(new ToolCall($"cli_{Guid.NewGuid():N}", name, arguments), token);
            if (outcome.AcceptedPlan is { } plan && Interlocked.CompareExchange(ref accepted, plan, null) is null)
                run.CancelAfter(StopGrace);
            return new McpToolResult(outcome.Output, outcome.IsError);
        }, run.Token);

        var configPath = Path.Combine(Path.GetTempPath(), "aithread", $"mcp-{session.Token}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, McpConfig($"{baseUrl}/mcp/planner/{session.Token}"), cancellationToken);

        try
        {
            string[] arguments =
            [
                "--mcp-config", configPath,
                "--strict-mcp-config",
                // --tools is the whole built-in tool set (no Edit, Write, Bash, web or sub-agents); dontAsk denies
                // anything not pre-approved instead of prompting, which a headless run could not answer.
                "--tools", string.Join(',', ReadOnlyTools),
                "--allowedTools", string.Join(',', ReadOnlyTools.Concat(McpTools.Select(McpToolName))),
                "--permission-mode", "dontAsk",
                "--max-turns", maxTurns.ToString(CultureInfo.InvariantCulture),
                // cwd, env and git status move from the system prompt into the first message, so the system prompt and
                // tools stay byte-identical across runs and projects and are read from the prompt cache instead of
                // being written again by every thread.
                "--exclude-dynamic-system-prompt-sections"
            ];
            var timeout = TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.ClaudeCli.TimeoutMinutes));
            var usage = new ClaudeCliUsage();

            ClaudeCliResult result;
            try
            {
                result = await runner.RunAsync(new ClaudeCliRun(node.Model, prompt, workingDirectory, arguments, timeout), onToolUse, usage, run.Token);
            }
            catch (OperationCanceledException) when (accepted is not null && !cancellationToken.IsCancellationRequested)
            {
                // Stopped by us after the plan was accepted; the API calls made up to that point are still counted.
                return new CliPlanningResult(accepted, string.Empty, usage.Calls, usage.Tokens);
            }

            if (accepted is not null) return new CliPlanningResult(accepted, result.Text, result.Turns, result.Tokens);
            if (result.IsError) throw new ClaudeCliException(DescribeError(result));
            return new CliPlanningResult(null, result.Text, result.Turns, result.Tokens);
        }
        finally
        {
            TryDelete(configPath);
        }
    }

    public static string McpToolName(string tool) => $"mcp__{McpServerName}__{tool}";

    // MCP tool list built from the same definitions the 9router planner gets, so both paths stay in sync.
    public static JsonArray ToolDefinitions()
    {
        var tools = new JsonArray();
        foreach (var definition in PlannerTools.All())
        {
            if (definition?["function"] is not JsonObject function) continue;
            var name = function["name"]?.GetValue<string>();
            if (name is null || !McpTools.Contains(name)) continue;
            tools.Add(new JsonObject
            {
                ["name"] = name,
                ["description"] = function["description"]?.DeepClone(),
                ["inputSchema"] = function["parameters"]?.DeepClone()
            });
        }
        return tools;
    }

    private static string McpConfig(string url) => new JsonObject
    {
        ["mcpServers"] = new JsonObject
        {
            [McpServerName] = new JsonObject { ["type"] = "http", ["url"] = url }
        }
    }.ToJsonString();

    private static string DescribeError(ClaudeCliResult result)
    {
        var detail = result.Text.Trim();
        if (result.Subtype == "error_max_turns") return $"Claude Code CLI mencapai batas {result.Turns} langkah tanpa memanggil submit_plan.";
        return string.IsNullOrEmpty(detail) ? $"Claude Code CLI gagal ({result.Subtype ?? "error"})." : $"Claude Code CLI gagal: {detail}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
