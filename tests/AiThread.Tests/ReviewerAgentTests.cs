using AiThread.Common.Enums;
using AiThread.Service.Orchestration;

namespace AiThread.Tests;

public class ReviewerAgentTests
{
    [Fact]
    public void Parses_issues_from_json_wrapped_in_prose()
    {
        var result = ReviewerAgent.Parse("""
            Here is my review:
            ```json
            {"approved": false, "summary": "Selector salah.", "issues": [
              {"file": "`wwwroot/js/user.js`", "anchor": "$('#txtDesc')", "problem": "Wrong id.", "fix": "Use #txtDescription."},
              {"file": "src/A.cs", "problem": "Missing using."},
              {"file": "", "problem": "no file"},
              {"file": "src/B.cs"}
            ]}
            ```
            """);

        Assert.False(result.Approved);
        Assert.Null(result.Error);
        Assert.Equal("Selector salah.", result.Summary);
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(new ReviewIssue("wwwroot/js/user.js", "$('#txtDesc')", "Wrong id.", "Use #txtDescription."), result.Issues[0]);
        Assert.Equal("Missing using.", result.Issues[1].Fix);
        Assert.Null(result.Issues[1].Anchor);
    }

    [Fact]
    public void Approval_follows_the_issue_list()
    {
        Assert.True(ReviewerAgent.Parse("""{"approved": false, "summary": "hmm", "issues": []}""").Approved);
        Assert.False(ReviewerAgent.Parse("""{"approved": true, "issues": [{"file": "a.cs", "problem": "p"}]}""").Approved);
    }

    [Theory]
    [InlineData("Looks good to me!")]
    [InlineData("{ not json at all }")]
    public void Unreadable_answers_count_as_no_review(string content)
    {
        var result = ReviewerAgent.Parse(content);

        Assert.NotNull(result.Error);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Input_names_every_task_with_its_diff_and_the_build()
    {
        var plan = new ThreadPlan
        {
            Summary = "Add description",
            Contract = [new ContractItem { Name = "UI input id", Value = "txtDescription" }],
            Tasks = [new PlanTask { Id = "T1", Title = "View", File = "Views/User/Index.cshtml", Instruction = "Add input." }]
        };
        TaskResult[] results =
        [
            new("T1", "Views/User/Index.cshtml", ThreadTaskStatus.Succeeded, "Added input.", "+<input id=\"txtDescription\" />\n", null, 100, 10),
            new("V1-1", "wwwroot/js/user.js", ThreadTaskStatus.Failed, null, null, "No valid SEARCH/REPLACE block.", 100, 10)
        ];

        var input = ReviewerAgent.BuildInput("Tambah deskripsi", plan, plan.Tasks, results, "Build `dotnet build App.slnx` succeeded in 3.0s.\n");

        Assert.Contains("- UI input id: txtDescription", input);
        Assert.Contains("## T1 · View — Succeeded", input);
        Assert.Contains("+<input id=\"txtDescription\" />", input);
        Assert.Contains("## V1-1 · wwwroot/js/user.js — Failed", input);
        Assert.Contains("Error: No valid SEARCH/REPLACE block.", input);
        Assert.Contains("succeeded in 3.0s", input);
        Assert.Contains("review the diffs only", ReviewerAgent.BuildInput("x", plan, plan.Tasks, results, null));
    }
}
