using System.Runtime.Versioning;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.UI.Hosting.Chat;

namespace AIThread.UI.Hosting.Agent;

// aithread setup auto: the node preset from an official CLI already signed in on this computer, so no 9router and no API
// key are needed. A complete set of nodes is left alone: it may be a team's 9router setup.
[SupportedOSPlatform("windows")]
public static class SetupAutoCommand
{
    public static async Task<int> RunAsync(int port, bool json)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!await Cli.EnsureServerAsync(port, Console.Error.WriteLine, Console.Error.WriteLine)) return ChatCommand.Failed;

        using var api = new ChatApiClient(port);
        try
        {
            var status = await api.GetSetupStatusAsync(CancellationToken.None);
            if (status.Steps.FirstOrDefault(s => s.Id == "nodes")?.Done == true)
                return Report(json, new { applied = false, reason = "nodes_ready" }, Lang.T("Node sudah lengkap; tidak ada yang diubah.", "The nodes are complete; nothing changed."));

            if (PickPreset(await api.GetCliEnginesAsync(CancellationToken.None)) is not { } preset)
            {
                Console.Error.WriteLine(Lang.T("Tidak ada CLI resmi yang terpasang (Claude Code, Gemini CLI, Codex). Pasang dan login ke salah satunya, atau hubungkan 9router lewat 'aithread setup'.",
                    "No official CLI is installed (Claude Code, Gemini CLI, Codex). Install and sign in to one of them, or connect 9router with 'aithread setup'."));
                if (json) AgentOutput.Write(new { applied = false, reason = "no_cli_engine" });
                return ChatCommand.Failed;
            }

            var result = await api.ApplyPresetAsync(preset, CancellationToken.None);
            return Report(json, new { applied = true, strongModel = preset.StrongModel, cheapModel = preset.CheapModel, nodes = result.Nodes.Select(n => n.Node.Name) },
                Lang.T($"Node dibuat dengan {preset.StrongModel}.", $"Nodes created with {preset.StrongModel}."));
        }
        catch (ChatApiException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ChatCommand.Failed;
        }
    }

    // Claude and Gemini have a strong and a cheap alias; Codex is one model. Antigravity's models differ per account.
    public static SetupPresetRequest? PickPreset(IReadOnlyList<CliBackendStatusDto> engines)
    {
        bool Has(string tool) => engines.Any(e => e.Installed && e.Tool == tool);
        if (Has("claude")) return new SetupPresetRequest("cli:claude:opus", "cli:claude:haiku");
        if (Has("gemini")) return new SetupPresetRequest("cli:gemini:pro", "cli:gemini:flash");
        return Has("codex") ? new SetupPresetRequest("cli:codex", null) : null;
    }

    private static int Report(bool json, object value, string text)
    {
        if (json) AgentOutput.Write(value);
        else Console.WriteLine(text);
        return ChatCommand.Ok;
    }
}
