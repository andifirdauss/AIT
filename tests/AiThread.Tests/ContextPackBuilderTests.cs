using AiThread.Service.Orchestration;

namespace AiThread.Tests;

public class ContextPackBuilderTests
{
    private static readonly Guid FirstId = Guid.Parse("3f2a1b9c-0000-4000-8000-000000000001");
    private static readonly Guid SecondId = Guid.Parse("7d41e0aa-0000-4000-8000-000000000002");

    private static TurnRecord Turn(Guid id, string prompt, string status = "Completed", string? error = null) => new(
        id,
        new DateTime(2026, 9, 29, 8, 35, 0, DateTimeKind.Utc),
        status,
        prompt,
        "Persist distributions on SAVE HEADER",
        [new ContractItem { Name = "JS function", Value = "p_saveDistribution" }],
        [new TurnTask("T1", "Services/PositionService.SaveData.cs", "edit", "Persist distributions", "Succeeded", "Loop now runs for SAVE HEADER")],
        "Distribution rows are saved now.",
        error);

    [Fact]
    public void Anchor_line_round_trips_to_the_short_id()
    {
        var line = ThreadAnchor.Line("MasterData.sln", FirstId);

        Assert.Equal("**AiThread** · `MasterData.sln` · thread `3f2a1b9c`", line);
        Assert.Equal(["3f2a1b9c"], ThreadAnchor.Find($"intro\n{line}\nmore"));
        Assert.Empty(ThreadAnchor.Find("thread `3f2a1b9c` mentioned without the AiThread header"));
    }

    [Fact]
    public void Incremental_input_carries_the_previous_pack_and_the_finished_turn()
    {
        var input = ContextPackBuilder.Input("## Goal\nFix distribution save", [], "[user] keep the old validation", Turn(SecondId, "ga ke save, bisa bantu cek"));

        Assert.Contains("# Current session context\n## Goal\nFix distribution save", input);
        Assert.Contains("keep the old validation", input);
        Assert.Contains("### thread 7d41e0aa · 2026-09-29 08:35 UTC · Completed", input);
        Assert.Contains("Request: ga ke save, bisa bantu cek", input);
        Assert.Contains("Contract: JS function = p_saveDistribution", input);
        Assert.Contains("- T1 edit `Services/PositionService.SaveData.cs` — Persist distributions [Succeeded]: Loop now runs for SAVE HEADER", input);
        Assert.DoesNotContain("# Earlier turns", input);
    }

    [Fact]
    public void Rebuild_input_lists_the_earlier_turns_instead_of_a_pack()
    {
        var input = ContextPackBuilder.Input(null, [Turn(FirstId, "first request")], null, Turn(SecondId, "second request"));

        Assert.Contains("# Earlier turns (oldest first)\n### thread 3f2a1b9c", input);
        Assert.Contains("# Turn that just finished\n### thread 7d41e0aa", input);
        Assert.DoesNotContain("# Current session context", input);
    }

    [Fact]
    public void Fallback_appends_turns_under_one_unsummarized_heading()
    {
        var once = ContextPackBuilder.Fallback("## Goal\nFix distribution save", [], Turn(FirstId, "first request"));
        var twice = ContextPackBuilder.Fallback(once, [], Turn(SecondId, "second request", "Failed", "Build error"));

        Assert.StartsWith("## Goal\nFix distribution save", twice);
        Assert.Equal(1, CountOf(twice, ContextPackBuilder.UnsummarizedHeading));
        Assert.Contains("- `thread 3f2a1b9c` (Completed) — first request → Persist distributions on SAVE HEADER; files: Services/PositionService.SaveData.cs [Succeeded]", twice);
        Assert.Contains("- `thread 7d41e0aa` (Failed) — second request", twice);
        Assert.Contains("error: Build error", twice);
    }

    [Fact]
    public void Fallback_without_a_pack_builds_a_work_log()
    {
        var pack = ContextPackBuilder.Fallback(null, [Turn(FirstId, "first request")], Turn(SecondId, "second request"));

        Assert.StartsWith("## Work log\n- `thread 3f2a1b9c`", pack);
        Assert.Contains("\n- `thread 7d41e0aa`", pack);
    }

    [Fact]
    public void Clean_strips_code_fences_and_rejects_empty_output()
    {
        Assert.Equal("## Goal\nx", ContextPackBuilder.Clean("```markdown\n## Goal\nx\n```"));
        Assert.Equal("## Goal\nx", ContextPackBuilder.Clean("  ## Goal\nx  "));
        Assert.Null(ContextPackBuilder.Clean("   "));
        Assert.Null(ContextPackBuilder.Clean(null));
    }

    [Fact]
    public void Oversized_pack_keeps_its_head_and_its_latest_part()
    {
        var pack = "## Goal\nKeep me" + new string('x', ContextPackBuilder.MaxPackChars) + "latest turn";

        var limited = ContextPackBuilder.Limit(pack);

        Assert.True(limited.Length <= ContextPackBuilder.MaxPackChars);
        Assert.StartsWith("## Goal\nKeep me", limited);
        Assert.EndsWith("latest turn", limited);
    }

    [Fact]
    public void Session_input_carries_the_previous_pack_files_and_digest()
    {
        var transcript = new TranscriptSummary("[user] Fix save\n[tool] Edit Services/A.cs", ["Fix save"], ["Services/A.cs"], 2);

        var input = ContextPackBuilder.SessionInput("## Goal\nFix save", transcript, @"D:\Proj");

        Assert.Contains("# Current session context\n## Goal\nFix save", input);
        Assert.Contains("Working directory: D:\\Proj", input);
        Assert.Contains("Files changed: Services/A.cs", input);
        Assert.EndsWith("[user] Fix save\n[tool] Edit Services/A.cs", input);
    }

    [Fact]
    public void Session_fallback_lists_the_latest_requests_and_files()
    {
        var transcript = new TranscriptSummary("", ["first", "second"], ["Services/A.cs"], 2);

        Assert.Equal("## Work log\n- Cleared session — requests: first | second; files changed: Services/A.cs",
            ContextPackBuilder.SessionFallback(null, transcript));
        Assert.Contains($"{ContextPackBuilder.UnsummarizedHeading}\n- Cleared session",
            ContextPackBuilder.SessionFallback("## Goal\nFix save", transcript));
    }

    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, string.Empty).Length) / value.Length;
}
