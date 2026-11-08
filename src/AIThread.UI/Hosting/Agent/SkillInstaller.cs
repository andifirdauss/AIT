using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Implementations.Cli;

namespace AIThread.UI.Hosting.Agent;

// Note: what the developer still has to do in that CLI.
public sealed record SkillInstallResult(string Cli, IReadOnlyList<string> Files, string? Note = null);

// Gives a coding CLI what the Claude Code plugin gives Claude Code: the aithread-orchestrator skill and the hook that
// holds file edits for AIT. The skill and the gate script are the plugin's own files, embedded in the exe. Hooks go into
// the CLI's user configuration, in the entries only AIT owns; opencode and oh-my-pi take a small plugin file instead.
public sealed class SkillInstaller(string home, string aitHome)
{
    public static readonly IReadOnlyList<string> Clis = ["codex", "gemini", "opencode", "qwen", "omp"];

    private const string SkillName = "aithread-orchestrator";
    private const string GatePlaceholder = "__AIT_GATE__";
    private const string SkillGateReference = "${CLAUDE_SKILL_DIR}/../../hooks/gate.mjs";
    private const string GateMarker = "AIThread/agent/gate.mjs";

    private static readonly string[] SharedSkillClis = ["codex", "gemini", "opencode"];

    public string GatePath => Path.Combine(aitHome, "agent", "gate.mjs");

    // The CLI's own folder exists: it has been used on this computer.
    public bool IsPresent(string cli) => Directory.Exists(cli switch
    {
        "codex" => Path.Combine(home, ".codex"),
        "gemini" => Path.Combine(home, ".gemini"),
        "opencode" => Path.Combine(home, ".config", "opencode"),
        "qwen" => Path.Combine(home, ".qwen"),
        _ => Path.Combine(home, ".omp")
    });

    public SkillInstallResult Install(string cli)
    {
        Write(GatePath, Resource("ait.gate.mjs"));
        var skill = Path.Combine(SkillRoot(cli), SkillName, "SKILL.md");
        Write(skill, Resource("ait.SKILL.md").Replace(SkillGateReference, GatePath.Replace('\\', '/')));

        switch (cli)
        {
            case "opencode":
                return new SkillInstallResult(cli, [skill, WriteAdapter(OpenCodePlugin, "ait.opencode.js")]);
            case "omp":
                return new SkillInstallResult(cli, [skill, WriteAdapter(OmpHook, "ait.omp.ts")]);
            case "codex":
                MergeHooks(CodexHooks, "UserPromptSubmit", "PreToolUse", "^apply_patch$", 3);
                return new SkillInstallResult(cli, [skill, CodexHooks], Lang.T(
                    "Codex tidak menjalankan hook baru sebelum disetujui: buka Codex, ketik /hooks, lalu setujui dua hook AIT.",
                    "Codex does not run a new hook until it is approved: open Codex, type /hooks, and approve the two AIT hooks."));
            case "gemini":
                MergeHooks(GeminiSettings, "BeforeAgent", "BeforeTool", "write_file|replace", 3000);
                return new SkillInstallResult(cli, [skill, GeminiSettings]);
            default:
                MergeHooks(QwenSettings, "UserPromptSubmit", "PreToolUse", "write_file|edit", 3);
                return new SkillInstallResult(cli, [skill, QwenSettings]);
        }
    }

    public SkillInstallResult Uninstall(string cli)
    {
        var hooks = HooksFile(cli);
        if (cli is not ("opencode" or "omp")) RemoveHooks(hooks);
        else if (File.Exists(hooks)) File.Delete(hooks);

        // The shared copy stays while another CLI still uses it.
        var skill = Path.Combine(SkillRoot(cli), SkillName);
        var inUse = SharedSkillClis.Contains(cli) && SharedSkillClis.Any(IsInstalled);
        if (!inUse && Directory.Exists(skill)) Directory.Delete(skill, recursive: true);
        return new SkillInstallResult(cli, [skill, hooks]);
    }

    private bool IsInstalled(string cli)
    {
        var hooks = HooksFile(cli);
        return File.Exists(hooks) && (cli == "opencode" || File.ReadAllText(hooks).Contains(GateMarker, StringComparison.OrdinalIgnoreCase));
    }

    private string HooksFile(string cli) => cli switch
    {
        "opencode" => OpenCodePlugin,
        "omp" => OmpHook,
        "codex" => CodexHooks,
        "gemini" => GeminiSettings,
        _ => QwenSettings
    };

    private string CodexHooks => Path.Combine(home, ".codex", "hooks.json");

    private string GeminiSettings => Path.Combine(home, ".gemini", "settings.json");

    private string QwenSettings => Path.Combine(home, ".qwen", "settings.json");

    private string OpenCodePlugin => Path.Combine(home, ".config", "opencode", "plugins", "aithread.js");

    private string OmpHook => Path.Combine(home, ".omp", "agent", "hooks", "pre", "aithread.ts");

    // Codex, Gemini CLI and opencode all read ~/.agents/skills, and a second copy elsewhere would be reported as a conflict.
    private string SkillRoot(string cli) => cli switch
    {
        "codex" or "gemini" or "opencode" => Path.Combine(home, ".agents", "skills"),
        "qwen" => Path.Combine(home, ".qwen", "skills"),
        "omp" => Path.Combine(home, ".omp", "agent", "skills"),
        _ => throw new ArgumentOutOfRangeException(nameof(cli), cli, null)
    };

    // Forward slashes read the same in cmd.exe, PowerShell and bash, whichever one the CLI runs its hooks in.
    private string HookCommand => $"node \"{GatePath.Replace('\\', '/')}\"";

    private string WriteAdapter(string path, string resource)
    {
        // As a JSON string: a Windows path has backslashes, and the placeholder sits inside quotes.
        var literal = JsonSerializer.Serialize(GatePath);
        Write(path, Resource(resource).Replace($"\"{GatePlaceholder}\"", literal));
        return path;
    }

    private void MergeHooks(string path, string promptEvent, string editEvent, string editMatcher, int timeout)
    {
        var root = ReadConfig(path);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        RemoveOwn(hooks);

        Add(hooks, promptEvent, null, HookCommand, timeout);
        Add(hooks, editEvent, editMatcher, HookCommand, timeout);
        WriteConfig(path, root);
    }

    private void RemoveHooks(string path)
    {
        if (!File.Exists(path)) return;
        var root = ReadConfig(path);
        if (root["hooks"] is not JsonObject hooks) return;

        RemoveOwn(hooks);
        if (hooks.Count == 0) root.Remove("hooks");
        WriteConfig(path, root);
    }

    private static JsonObject ReadConfig(string path) =>
        JsonConfigFile.ReadForWrite(File.Exists(path) ? File.ReadAllText(path) : string.Empty, Path.GetFileName(path));

    private static void WriteConfig(string path, JsonObject root)
    {
        if (File.Exists(path) && !File.Exists(path + ".aithread-backup")) File.Copy(path, path + ".aithread-backup");
        Write(path, JsonConfigFile.Write(root));
    }

    private static void Add(JsonObject hooks, string eventName, string? matcher, string command, int timeout)
    {
        var groups = hooks[eventName] as JsonArray ?? new JsonArray();
        hooks[eventName] = groups;

        var group = new JsonObject();
        if (matcher is not null) group["matcher"] = matcher;
        group["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = timeout });
        groups.Add(group);
    }

    // Every group whose command runs AIT's gate, wherever it was installed from; the developer's own hooks stay.
    private static void RemoveOwn(JsonObject hooks)
    {
        foreach (var name in hooks.Select(pair => pair.Key).ToList())
        {
            if (hooks[name] is not JsonArray groups) continue;
            for (var i = groups.Count - 1; i >= 0; i--)
                if (IsOwn(groups[i])) groups.RemoveAt(i);
            if (groups.Count == 0) hooks.Remove(name);
        }
    }

    private static bool IsOwn(JsonNode? group) =>
        group?["hooks"] is JsonArray list && list.Any(hook =>
            JsonConfigFile.GetString(hook?["command"])?.Replace('\\', '/').Contains(GateMarker, StringComparison.OrdinalIgnoreCase) == true);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded resource {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
