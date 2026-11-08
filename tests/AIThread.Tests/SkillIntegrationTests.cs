using System.Text.Json;
using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.UI.Hosting;
using AIThread.UI.Hosting.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace AIThread.Tests;

// What the Claude Code plugin leans on: the gate behind its hooks and the commands an AI agent drives.
public class SkillIntegrationTests
{
    private static readonly DoctorFacts AllGood = new(true, true, true, true, true, true, true, true);

    [Fact]
    public void Agent_options_are_parsed_next_to_the_command_arguments()
    {
        var options = Cli.Options.Parse(["wait", "abc", "--timeout", "120", "--json", "--detach", "--start"]);

        Assert.Null(options.Error);
        Assert.Equal(["wait", "abc"], options.Rest);
        Assert.Equal(120, options.Timeout);
        Assert.True(options.Json);
        Assert.True(options.Detach);
        Assert.True(options.Start);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3601")]
    [InlineData("soon")]
    public void A_timeout_outside_the_range_is_refused(string value)
    {
        using var language = Lang.Use("en");
        Assert.Contains("--timeout", Cli.Options.Parse(["--timeout", value]).Error);
    }

    [Theory]
    [InlineData(ThreadStatus.Completed, false, false, 0)]
    [InlineData(ThreadStatus.Failed, false, false, 1)]
    [InlineData(ThreadStatus.Cancelled, false, false, 1)]
    [InlineData(ThreadStatus.RolledBack, false, false, 1)]
    [InlineData(ThreadStatus.AwaitingInput, true, false, 3)]
    [InlineData(ThreadStatus.Completed, true, false, 3)]
    [InlineData(ThreadStatus.Executing, false, true, 4)]
    public void Thread_wait_exit_codes(ThreadStatus status, bool waiting, bool timedOut, int expected) =>
        Assert.Equal(expected, ThreadCommand.ExitCodeOf(status, waiting, timedOut));

    [Fact]
    public void Only_the_three_plain_answers_can_be_given_from_the_command_line()
    {
        Assert.Equal(ApprovalDecision.Once, ThreadCommand.ParseDecision("once"));
        Assert.Equal(ApprovalDecision.Always, ThreadCommand.ParseDecision("ALWAYS"));
        Assert.Equal(ApprovalDecision.Deny, ThreadCommand.ParseDecision("deny"));
        Assert.Null(ThreadCommand.ParseDecision("all"));
    }

    [Fact]
    public void Report_says_what_the_thread_waits_for_and_serializes_the_status_by_name()
    {
        var approval = new PendingApprovalDto([], "reason", true, "card", false);
        var report = ThreadCommand.ReportOf(Detail(ThreadStatus.AwaitingInput), "http://x", false, approval, false);

        Assert.Equal("waiting", report.Outcome);
        Assert.Equal("approval", report.WaitingFor);
        Assert.Contains("\"status\":\"AwaitingInput\"", AgentOutput.Serialize(report));

        Assert.Equal("question", ThreadCommand.ReportOf(Detail(ThreadStatus.AwaitingInput), "http://x", false, null, true).WaitingFor);
        Assert.Equal("running", ThreadCommand.ReportOf(Detail(ThreadStatus.Executing), "http://x", true, null, false).Outcome);
        Assert.Equal("completed", ThreadCommand.ReportOf(Detail(ThreadStatus.Completed), "http://x", false, null, false).Outcome);
        Assert.Equal("failed", ThreadCommand.ReportOf(Detail(ThreadStatus.Failed), "http://x", false, null, false).Outcome);
    }

    [Fact]
    public void Doctor_names_one_next_step_in_dependency_order()
    {
        Assert.Empty(DoctorCommand.FixesFor(AllGood));
        Assert.Equal("install-localdb", OnlyFix(new DoctorFacts(true, false, false, false, false, false, false, false)));
        Assert.Equal("start-server", OnlyFix(new DoctorFacts(false, false, false, false, false, false, true, false)));
        Assert.Equal("database", OnlyFix(AllGood with { SystemReady = false, NodesReady = false }));
        Assert.Equal("setup-nodes", OnlyFix(AllGood with { NodesReady = false, ProjectRegistered = false }));
        Assert.Equal("no-model", OnlyFix(AllGood with { NodesReady = false, EngineInstalled = false }));
        Assert.Equal("router", OnlyFix(AllGood with { RouterReady = false, ProjectRegistered = false }));
        Assert.Equal("register-project", OnlyFix(AllGood with { ProjectRegistered = false }));
    }

    [Fact]
    public void Setup_auto_picks_the_first_installed_cli_that_has_known_models()
    {
        static CliBackendStatusDto Engine(string tool, bool installed = true) => new(tool, tool, installed, null, null, null);

        Assert.Equal(new SetupPresetRequest("cli:claude:opus", "cli:claude:haiku"), SetupAutoCommand.PickPreset([Engine("codex"), Engine("claude")]));
        Assert.Equal(new SetupPresetRequest("cli:gemini:pro", "cli:gemini:flash"), SetupAutoCommand.PickPreset([Engine("claude", false), Engine("gemini")]));
        Assert.Equal(new SetupPresetRequest("cli:codex", null), SetupAutoCommand.PickPreset([Engine("codex")]));
        Assert.Null(SetupAutoCommand.PickPreset([Engine("agy"), Engine("claude", false)]));
    }

    [Fact]
    public void Turning_the_plugin_on_clears_the_refusal_and_every_entry_around_the_folder()
    {
        var settings = new SkillSettings { Declined = true };
        settings.Disable(@"C:\work\app");
        settings.Disable(@"C:\work\app\web");
        settings.Disable(@"C:\work\other");

        Assert.Equal(2, settings.DisabledRoots.Count);
        Assert.True(settings.IsDisabled(@"C:\WORK\app\src"));
        Assert.False(settings.IsDisabled(@"C:\work\app2"));

        settings.Enable(@"C:\work\app\web");

        Assert.False(settings.Declined);
        Assert.Equal([@"C:\work\other"], settings.DisabledRoots);
    }

    [Fact]
    public async Task Gate_follows_the_folder_and_the_pipeline()
    {
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: _ => { });
        var gate = harness.Service<ISkillGateService>();
        using var workspace = new TempWorkspace();
        var project = Path.Combine(workspace.Root, "shop");
        Directory.CreateDirectory(Path.Combine(project, "src"));
        await File.WriteAllTextAsync(Path.Combine(project, "package.json"), "{}");
        var plain = Path.Combine(workspace.Root, "notes");
        Directory.CreateDirectory(plain);

        Assert.Equal(SkillGateState.NotAProject, (await gate.GetAsync(plain)).State);
        Assert.Equal(SkillGateState.SetupIncomplete, (await gate.GetAsync(project)).State);

        foreach (var role in new[] { NodeRole.Planner, NodeRole.Executor, NodeRole.Wrapper }) await harness.AddNodeAsync(role, "model");

        var unknown = await gate.GetAsync(Path.Combine(project, "src"));
        Assert.Equal(SkillGateState.ProjectNotRegistered, unknown.State);
        Assert.Equal("shop", unknown.ProjectName);

        await harness.Service<IProjectService>().RegisterAsync(project);
        var ready = await gate.GetAsync(Path.Combine(project, "src", "not-created-yet"));
        Assert.Equal(SkillGateState.Ready, ready.State);
        Assert.Equal(project, ready.ProjectRoot, ignoreCase: true);
    }

    [Theory]
    [InlineData("codex", ".codex/hooks.json", "UserPromptSubmit", "PreToolUse", "^apply_patch$", ".agents/skills")]
    [InlineData("gemini", ".gemini/settings.json", "BeforeAgent", "BeforeTool", "write_file|replace", ".agents/skills")]
    [InlineData("qwen", ".qwen/settings.json", "UserPromptSubmit", "PreToolUse", "write_file|edit", ".qwen/skills")]
    public void Hooks_join_the_developers_own_and_leave_with_only_ours_gone(string cli, string config, string promptEvent, string editEvent, string matcher, string skills)
    {
        using var workspace = new TempWorkspace();
        var installer = new SkillInstaller(workspace.Root, Path.Combine(workspace.Root, "AIThread"));
        var path = Path.Combine(workspace.Root, config);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var own = $$$"""{"model":"x","hooks":{"{{{editEvent}}}":[{"matcher":"mine","hooks":[{"type":"command","command":"mine.sh"}]}]}}""";
        File.WriteAllText(path, own);

        installer.Install(cli);
        installer.Install(cli);

        var root = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("x", root["model"]!.GetValue<string>());
        Assert.Single(root["hooks"]![promptEvent]!.AsArray());
        var edits = root["hooks"]![editEvent]!.AsArray();
        Assert.Equal(["mine", matcher], edits.Select(g => g!["matcher"]!.GetValue<string>()));
        Assert.Contains("AIThread/agent/gate.mjs", edits[1]!["hooks"]![0]!["command"]!.GetValue<string>());
        Assert.True(File.Exists(installer.GatePath));
        Assert.True(File.Exists(path + ".aithread-backup"));
        var skill = File.ReadAllText(Path.Combine(workspace.Root, skills, "aithread-orchestrator", "SKILL.md"));
        Assert.DoesNotContain("CLAUDE_SKILL_DIR", skill);
        Assert.Contains("AIThread/agent/gate.mjs", skill);

        installer.Uninstall(cli);

        var after = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Null(after["hooks"]![promptEvent]);
        Assert.Equal("mine", Assert.Single(after["hooks"]![editEvent]!.AsArray())!["matcher"]!.GetValue<string>());
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, skills, "aithread-orchestrator")));
    }

    [Theory]
    [InlineData("opencode", ".config/opencode/plugins/aithread.js")]
    [InlineData("omp", ".omp/agent/hooks/pre/aithread.ts")]
    public void In_process_clis_get_a_plugin_file_that_names_the_gate(string cli, string plugin)
    {
        using var workspace = new TempWorkspace();
        var installer = new SkillInstaller(workspace.Root, Path.Combine(workspace.Root, "AIThread"));
        var path = Path.Combine(workspace.Root, plugin);

        installer.Install(cli);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("__AIT_GATE__", text);
        Assert.Contains(JsonSerializer.Serialize(installer.GatePath), text);

        installer.Uninstall(cli);
        installer.Uninstall(cli);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void The_shared_skill_stays_until_the_last_cli_using_it_is_removed()
    {
        using var workspace = new TempWorkspace();
        var installer = new SkillInstaller(workspace.Root, Path.Combine(workspace.Root, "AIThread"));
        var skill = Path.Combine(workspace.Root, ".agents", "skills", "aithread-orchestrator");
        installer.Install("codex");
        installer.Install("opencode");

        installer.Uninstall("codex");
        Assert.True(Directory.Exists(skill));

        installer.Uninstall("opencode");
        Assert.False(Directory.Exists(skill));
    }

    [Fact]
    public void A_config_file_with_comments_is_refused_and_left_alone()
    {
        using var workspace = new TempWorkspace();
        var path = Path.Combine(workspace.Root, ".qwen", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string commented = "{ // mine\n \"a\": 1 }";
        File.WriteAllText(path, commented);

        Assert.Throws<AppValidationException>(() => new SkillInstaller(workspace.Root, Path.Combine(workspace.Root, "AIThread")).Install("qwen"));

        Assert.Equal(commented, File.ReadAllText(path));
    }

    private static string OnlyFix(DoctorFacts facts) => Assert.Single(DoctorCommand.FixesFor(facts)).Id;

    private static ThreadDetailDto Detail(ThreadStatus status) => new(
        new ThreadSummaryDto(Guid.NewGuid(), null, null, ThreadSource.Dashboard, "chat:1", "prompt", status, null, 0, 0, 0, null, null, null, 0, DateTime.UtcNow, null),
        null, "answer", null, null, null, 0, 0, 0, 0, null, null, null, []);
}
