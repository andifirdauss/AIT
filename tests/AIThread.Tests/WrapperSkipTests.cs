using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Service.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace AIThread.Tests;

public class WrapperSkipTests
{
    private const int Limit = 2;
    private const string Change = "Ganti sapaan jadi Halo";

    private static ExecutionSummary Tasks(params ThreadTaskStatus[] statuses) =>
        new(statuses.Select((s, i) => new TaskResult($"T{i + 1}", "a.cs", s, null, null, null, 1, 1)).ToList(), 1, 1, 1);

    private static ExecutionSummary Done(int count = 1) => Tasks(Enumerable.Repeat(ThreadTaskStatus.Succeeded, count).ToArray());

    private static VerificationReport Verified(string outcome = VerificationReport.Passed, int rounds = 1) =>
        new() { Outcome = outcome, Rounds = Enumerable.Range(1, rounds).Select(r => new VerificationRound { Round = r }).ToList() };

    [Fact]
    public void Small_change_that_passed_needs_no_wrapper()
    {
        Assert.True(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(), null, null));
        Assert.True(WrapperAgent.CanSkip(Limit, Change,Done(Limit), Verified(), new TestingReport { Outcome = TestingReport.Passed }, new UnitTestReport { Outcome = UnitTestReport.Passed }));
        // Nothing to verify with (no build command, no reviewer).
        Assert.True(WrapperAgent.CanSkip(Limit, Change,Done(), null, null, null));
    }

    [Fact]
    public void Anything_left_to_explain_keeps_the_wrapper()
    {
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(Limit + 1), Verified(), null, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Tasks(ThreadTaskStatus.Succeeded, ThreadTaskStatus.Failed), Verified(), null, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(VerificationReport.Failed), null, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(VerificationReport.Unverified), null, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(rounds: 2), null, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(), new TestingReport { Outcome = TestingReport.Failed }, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(), new TestingReport { Outcome = TestingReport.Blocked }, null));
        Assert.False(WrapperAgent.CanSkip(Limit, Change,Done(), Verified(), null, new UnitTestReport { Outcome = UnitTestReport.Failed }));
    }

    [Fact]
    public void Limit_of_zero_always_keeps_the_wrapper() =>
        Assert.False(WrapperAgent.CanSkip(0, Change, Done(), Verified(), null, null));

    [Theory]
    [InlineData("Ganti sapaan jadi Halo lalu jelaskan alasannya")]
    [InlineData("Kenapa harus diubah begini")]
    [InlineData("Ganti sapaan, sudah benar belum?")]
    [InlineData("Change the greeting and explain the reason")]
    [InlineData("WHY does this need a change")]
    public void A_prompt_that_asks_for_an_explanation_keeps_the_wrapper(string prompt) =>
        Assert.False(WrapperAgent.CanSkip(Limit, prompt, Done(), Verified(), null, null));

    [Fact]
    public async Task Small_change_ends_with_the_plan_summary_without_calling_the_wrapper()
    {
        await using var harness = await OrchestrationHarness.CreateAsync(configureServices: s => s.Configure<AIThreadOptions>(o => o.WrapperSkipMaxTasks = Limit));
        harness.Project.Write(ThreadOrchestratorTests.GreeterFile, ThreadOrchestratorTests.Greeter);
        await harness.AddStandardNodesAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan diganti jadi Halo.",
                Tasks = [ThreadOrchestratorTests.Step("T1", ThreadOrchestratorTests.GreeterFile, "Return \"Halo\" from Hello().")]
            }))
            .OnExecutor(ThreadOrchestratorTests.GreeterFile, ScriptedRouter.Edit(ThreadOrchestratorTests.HelloLine, ThreadOrchestratorTests.HalloLine, "Sapaan diganti."));

        var (result, observer) = await harness.RunAsync("Ganti sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Empty(router.CallsTo(ScriptedAgent.Wrapper));
        Assert.Contains("Sapaan diganti jadi Halo.", observer.Final);
    }
}
