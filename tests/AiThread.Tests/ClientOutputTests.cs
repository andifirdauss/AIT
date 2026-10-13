using AiThread.Common.Enums;
using AiThread.Service.Orchestration;

namespace AiThread.Tests;

// What the client sees: the progress stepper (ClientProgress) and the parts of the final answer AIT writes itself
// (FinalReport).
public class ClientOutputTests
{
    private const string Anchor = "**AIT** · `Demo` · thread `abcd1234`";

    [Fact]
    public async Task Stepper_sits_in_a_text_block_that_closes_before_the_answer()
    {
        var progress = new ClientProgress(new RecordingObserver(), showProgress: true);

        await progress.AnchorAsync(Anchor, CancellationToken.None);
        await progress.PhaseAsync("🧠", "Planner · planner-model", CancellationToken.None);
        await progress.StepAsync(ClientProgress.Pad("read_file", 16) + "`src/A.cs`", CancellationToken.None, hang: 16);
        await progress.StepAsync("selesai dalam 1.0s", CancellationToken.None, last: true);
        await progress.PhaseAsync("📋", "Rencana · 1 tugas", CancellationToken.None);
        await progress.StepAsync("T1 src/A.cs", CancellationToken.None, last: true, hang: 3);
        await progress.DetailAsync("Ganti sapaan", CancellationToken.None, 3, underLast: true);
        await progress.FinalAsync("Jawaban.", CancellationToken.None);

        Assert.Equal(
            Anchor + "\n" +
            "````text\n" +
            "🧠 Planner · planner-model\n" +
            "│  ├─ read_file       src/A.cs\n" +
            "│  └─ selesai dalam 1.0s\n" +
            "│\n" +
            "📋 Rencana · 1 tugas\n" +
            "│  └─ T1 src/A.cs\n" +
            "│        Ganti sapaan\n" +
            "````\n\n---\n\nJawaban.",
            progress.Transcript);
    }

    [Fact]
    public async Task Hidden_progress_keeps_the_anchor_without_a_block()
    {
        var progress = new ClientProgress(new RecordingObserver(), showProgress: false);

        await progress.AnchorAsync(Anchor, CancellationToken.None);
        await progress.PhaseAsync("🧠", "Planner · planner-model", CancellationToken.None);
        await progress.StepAsync("read_file", CancellationToken.None);
        await progress.FinalAsync("Jawaban.", CancellationToken.None);

        Assert.Equal(Anchor + "\n\n---\n\nJawaban.", progress.Transcript);
    }

    [Fact]
    public async Task Progress_after_the_answer_started_is_dropped_and_close_ends_an_open_block()
    {
        var observer = new RecordingObserver();
        var progress = new ClientProgress(observer, showProgress: true);
        await progress.PhaseAsync("🧠", "Planner · m", CancellationToken.None);
        await progress.FinalAsync("Jawaban.", CancellationToken.None);
        await progress.StepAsync("terlambat", CancellationToken.None);
        Assert.DoesNotContain("terlambat", progress.Transcript);

        var cancelled = new ClientProgress(new RecordingObserver(), showProgress: true);
        await cancelled.PhaseAsync("🧠", "Planner · m", CancellationToken.None);
        await cancelled.CloseAsync();
        Assert.EndsWith("🧠 Planner · m\n````\n", cancelled.Transcript);
    }

    [Fact]
    public void Wrap_keeps_lines_within_the_width_with_the_tree_prefix()
    {
        var text = string.Join(' ', Enumerable.Repeat("kata", 40));

        var lines = ClientProgress.Wrap(text, "│  ├─ ", "│  │  ").TrimEnd('\n').Split('\n');

        Assert.True(lines.Length > 1);
        Assert.StartsWith("│  ├─ kata", lines[0]);
        Assert.All(lines.Skip(1), l => Assert.StartsWith("│  │  kata", l));
        Assert.All(lines, l => Assert.True(ClientProgress.DisplayWidth(l) <= ClientProgress.Width, l));
    }

    [Fact]
    public void Wrap_keeps_column_padding_and_cuts_words_longer_than_a_line()
    {
        Assert.Equal("│  ├─ ✅ T1    3.1s  exec  ok\n", ClientProgress.Wrap("✅ T1    3.1s  exec  ok", "│  ├─ ", "│  │  "));
        Assert.Equal(2, ClientProgress.DisplayWidth("✅"));
        Assert.Equal(2, ClientProgress.DisplayWidth("🚀"));

        var path = "src/" + new string('a', 120) + ".cs";
        var lines = ClientProgress.Wrap(path, "│  ├─ ", "│  │  ").TrimEnd('\n').Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.True(ClientProgress.DisplayWidth(l) <= ClientProgress.Width, l));
        Assert.Equal(path, lines[0][6..] + lines[1][6..]);
    }

    [Fact]
    public void Status_counts_planned_tasks_and_lists_what_needs_attention()
    {
        var execution = Summary(
            new TaskResult("T1", "src/A.cs", ThreadTaskStatus.Succeeded, "ok", Edit("src/A.cs"), null, 1, 1),
            new TaskResult("T2", "src/B.js", ThreadTaskStatus.Failed, null, null, "SEARCH block did not match", 1, 1),
            new TaskResult("V1-1", "src/A.cs", ThreadTaskStatus.Succeeded, "fixed", Edit("src/A.cs"), null, 1, 1));
        var verification = new VerificationReport
        {
            Outcome = VerificationReport.Passed,
            Rounds =
            [
                new VerificationRound { Round = 1, Build = new BuildRoundReport { Status = "failed", ErrorCount = 1 }, FixTasks = ["V1-1"] },
                new VerificationRound { Round = 2, Build = new BuildRoundReport { Status = "passed" }, Review = new ReviewRoundReport { Approved = true } }
            ]
        };

        var status = FinalReport.Status(execution, verification);

        Assert.StartsWith("🟡 **Selesai sebagian** · 1/2 tugas · build lolos · reviewer setuju\n\n**Perlu perhatian**\n", status);
        Assert.Contains("- ❌ **T2** `src/B.js`: SEARCH block did not match\n", status);
        Assert.DoesNotContain("V1-1", status);
    }

    [Fact]
    public void Status_of_a_clean_run_has_no_attention_list()
    {
        var execution = Summary(new TaskResult("T1", "src/A.cs", ThreadTaskStatus.Succeeded, "ok", Edit("src/A.cs"), null, 1, 1));

        Assert.Equal("✅ **Selesai** · 1/1 tugas\n\n", FinalReport.Status(execution, null));
    }

    [Fact]
    public void Changed_files_merge_the_tasks_of_a_file_and_read_the_kind_of_change_from_the_diff()
    {
        var execution = Summary(
            new TaskResult("T1", "src/New.cs", ThreadTaskStatus.Succeeded, "ok", "--- /dev/null\n+++ b/src/New.cs\n@@ block 1 @@\n+a\n+b\n+c\n", null, 1, 1),
            new TaskResult("T2", "src/A.cs", ThreadTaskStatus.Succeeded, "ok", Edit("src/A.cs"), null, 1, 1),
            new TaskResult("T3", "src/Old.cs", ThreadTaskStatus.Succeeded, "ok", "--- a/src/Old.cs\n+++ b/src/Moved.cs\n@@ file moved, content unchanged @@\n", null, 1, 1),
            new TaskResult("T4", "src/Gone.cs", ThreadTaskStatus.Succeeded, "ok", "--- a/src/Gone.cs\n+++ /dev/null\n@@ file deleted @@\n", null, 1, 1),
            new TaskResult("T5", "src/Skip.cs", ThreadTaskStatus.Failed, null, null, "no", 1, 1),
            new TaskResult("V1-1", "src/New.cs", ThreadTaskStatus.Succeeded, "fix", "--- a/src/New.cs\n+++ b/src/New.cs\n@@ block 1 @@\n-b\n+B\n", null, 1, 1));

        var table = FinalReport.ChangedFiles(execution);

        Assert.Contains("| File | Aksi | Baris | Tugas |", table);
        Assert.Contains("| `src/New.cs` | baru | +4 −1 | T1, V1-1 |", table);
        Assert.Contains("| `src/A.cs` | ubah | +1 −1 | T2 |", table);
        Assert.Contains("| `src/Old.cs → src/Moved.cs` | pindah | — | T3 |", table);
        Assert.Contains("| `src/Gone.cs` | hapus | — | T4 |", table);
        Assert.DoesNotContain("Skip.cs", table);
        Assert.Equal(string.Empty, FinalReport.ChangedFiles(Summary(new TaskResult("T1", "a", ThreadTaskStatus.Failed, null, null, "x", 1, 1))));
    }

    [Fact]
    public void Footer_shows_time_tokens_memory_wiki_and_the_thread_link()
    {
        var id = Guid.Parse("3f2a1b9c-0000-0000-0000-000000000000");

        var footer = FinalReport.Footer(TimeSpan.FromSeconds(72), 48_200, 2, new MemoryExtraction(3, 100), false, 6, "http://localhost:5080/", id);

        Assert.Contains("⏱️  1m 12s · 🪙  48.2k token · 🧠 2 memory dipakai, +3 memory baru · 📚 Wiki masuk antrian rangkuman berikutnya (tiap 6 jam)\n", footer);
        Assert.Contains($"🔗 Detail & rollback: http://localhost:5080/Thread/Detail/{id}\n", footer);

        var pending = FinalReport.Footer(TimeSpan.FromSeconds(9.5), 850, 0, null, true, null, null, id);
        Assert.Equal("\n\n⏱️  9.5s · 🪙  850 token · 🧠 memory baru masih diproses\n", pending);
    }

    private static string Edit(string file) => $"--- a/{file}\n+++ b/{file}\n@@ block 1 @@\n-old\n+new\n";

    private static ExecutionSummary Summary(params TaskResult[] results) => new(results, 1, results.Length, 0);
}
