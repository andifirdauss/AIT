using AiThread.Common.DTOs;
using AiThread.Service.Implementations;
using AiThread.Service.Orchestration;

namespace AiThread.Tests;

// Remaining provider quota in the answer's footer: which 9router accounts serve the nodes' models, the parsing of
// 9router's usage API, and the footer lines.
public class RouterQuotaTests
{
    private static readonly RouterConnection Claude = new("c1", "claude", "Andi");
    private static readonly RouterConnection Antigravity = new("a1", "antigravity", "DS997");

    [Fact]
    public void Node_models_and_combo_members_map_to_providers_by_alias_or_id()
    {
        var combos = new Dictionary<string, IReadOnlyList<string>> { ["smart"] = ["cc/claude-opus-5-5", "gemini-cli/gemini-3-pro"] };

        var byPrefix = RouterQuotas.ModelsByPrefix(["ag/gemini-3.8-flash-high", "smart", "bare-model", " "], combos);

        Assert.Equal(["gemini-3.8-flash-high"], RouterQuotas.UsedModels("antigravity", byPrefix));
        Assert.Equal(["claude-opus-5-5"], RouterQuotas.UsedModels("claude", byPrefix));
        Assert.Equal(["gemini-3-pro"], RouterQuotas.UsedModels("gemini-cli", byPrefix));
        Assert.Null(RouterQuotas.UsedModels("codex", byPrefix));
    }

    [Fact]
    public void Connections_skip_inactive_accounts_and_read_the_page_count()
    {
        const string json = """
            {"connections":[
              {"id":"c1","provider":"claude","name":"Andi","isActive":true},
              {"id":"c2","provider":"claude","name":"Off","isActive":false},
              {"id":"a1","provider":"antigravity","email":"x@example.com"}],
             "pagination":{"page":1,"totalPages":2}}
            """;

        var connections = RouterQuotas.ParseConnections(json, out var pages);

        Assert.Equal(2, pages);
        Assert.Equal([new RouterConnection("c1", "claude", "Andi"), new RouterConnection("a1", "antigravity", "x@example.com")], connections);
    }

    [Fact]
    public void Time_window_quotas_are_all_kept()
    {
        const string json = """
            {"plan":"Claude Code","quotas":{
              "session (5h)":{"used":83,"total":100,"remaining":17,"remainingPercentage":17,"resetAt":"2026-10-15T04:10:00.364Z","unlimited":false},
              "weekly (7d)":{"used":31,"total":100,"resetAt":"2026-10-19T23:00:00Z"}}}
            """;

        var quota = RouterQuotas.ParseUsage(json, Claude, new HashSet<string> { "claude-opus-5-5" })!;

        Assert.Equal("Claude Code", quota.Plan);
        Assert.Equal("Andi", quota.Name);
        Assert.Equal(["session (5h)", "weekly (7d)"], quota.Windows.Select(w => w.Name));
        Assert.Equal(17, quota.Windows[0].RemainingPercent);
        Assert.Equal(69, quota.Windows[1].RemainingPercent);
        Assert.Equal(new DateTime(2026, 10, 15, 4, 10, 0, 364, DateTimeKind.Utc), quota.Windows[0].ResetAt);
    }

    [Fact]
    public void Per_model_quotas_keep_only_the_models_the_nodes_use()
    {
        const string json = """
            {"plan":"Antigravity","quotas":{
              "claude-sonnet-4-6":{"used":0,"total":1000,"remainingPercentage":100},
              "gemini-3.8-flash-high":{"used":13,"total":1000,"remainingPercentage":98.7,"displayName":"Gemini 3.8 Flash (High)"}}}
            """;

        var quota = RouterQuotas.ParseUsage(json, Antigravity, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gemini-3.8-flash-high" })!;

        var window = Assert.Single(quota.Windows);
        Assert.Equal("Gemini 3.8 Flash (High)", window.Name);
    }

    [Fact]
    public void Many_unmatched_limits_show_the_lowest_first()
    {
        var quotas = string.Join(",", Enumerable.Range(1, 6).Select(i => $"\"m{i}\":{{\"remainingPercentage\":{i * 10}}}"));

        var quota = RouterQuotas.ParseUsage($"{{\"quotas\":{{{quotas}}}}}", Antigravity, new HashSet<string> { "other" })!;

        Assert.Equal("antigravity", quota.Plan);
        Assert.Equal(["m1", "m2", "m3", "m4"], quota.Windows.Select(w => w.Name));
    }

    [Theory]
    [InlineData("""{"message":"Usage not available for this connection"}""")]
    [InlineData("""{"error":"Credential refresh failed"}""")]
    [InlineData("""{"plan":"x","quotas":{}}""")]
    public void Accounts_without_limits_are_left_out(string json) =>
        Assert.Null(RouterQuotas.ParseUsage(json, Claude, new HashSet<string>()));

    [Fact]
    public void Combos_accept_plain_and_object_members()
    {
        var combos = RouterQuotas.ParseCombos("""{"combos":[{"name":"smart","models":["cc/a",{"model":"ag/b"}]},{"models":["cc/x"]}]}""");

        Assert.Equal(["cc/a", "ag/b"], Assert.Single(combos).Value);
    }

    [Fact]
    public void Footer_lists_remaining_quota_before_the_thread_link()
    {
        var now = new DateTime(2026, 10, 15, 2, 50, 0, DateTimeKind.Utc);
        var quotas = new List<RouterQuotaDto>
        {
            new("c1", "claude", "Andi", "Claude Code",
            [
                new("session (5h)", 17, false, now.AddMinutes(80)),
                new("weekly (7d)", 69.4, false, now.AddDays(4).AddHours(20))
            ]),
            new("a1", "antigravity", "DS997", "Antigravity", [new("Gemini 3.8 Flash (High)", 0, false, now.AddMinutes(-5))]),
            new("g1", "github", "github", "github", [new("chat", null, true, null)])
        };

        var footer = FinalReport.Footer(TimeSpan.FromSeconds(5), 100, 0, null, false, null, "http://localhost:5225", Guid.Empty, quotas, now);

        Assert.Contains("""
            📊 **Sisa kuota**
            - Antigravity · DS997: Gemini 3.8 Flash (High) 0% habis
            - Claude Code · Andi: session (5h) 17% ⚠️ (reset 1j 20m) · weekly (7d) 69% (reset 4h 20j)
            - github: chat tanpa batas

            🔗 Detail & rollback:
            """.ReplaceLineEndings("\n"), footer);
    }
}
