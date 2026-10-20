using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

// The parts of the final answer AIT writes itself around the wrapper's text, from the results and never from a model:
// a status line with what still needs attention, the table of changed files, and the footer (time, tokens, memory,
// wiki, remaining provider quota, dashboard link).
public static partial class FinalReport
{
    private const int MaxAttentionErrors = 10;

    // Quota bars: one cell per 10%, coloured by what remains (terminals only colour emoji).
    private const int QuotaBarCells = 10;
    private const string QuotaEmptyCell = "⬜";

    // "✅ **Selesai** · 8/8 tugas · build lolos · reviewer setuju", then "Perlu perhatian" when something failed.
    // Fix tasks (V<round>-<n>) do not count as tasks of the plan; their failures are listed like the others.
    public static string Status(ExecutionSummary execution, VerificationReport? verification)
    {
        var planned = execution.Results.Where(r => !IsFixTask(r.TaskKey)).ToList();
        var succeeded = planned.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var verificationFailed = verification?.Outcome == VerificationReport.Failed;
        var (icon, label) = execution.Results.All(r => r.Status != ThreadTaskStatus.Succeeded) ? ("❌", "Gagal")
            : succeeded < planned.Count || verificationFailed ? ("🟡", "Selesai sebagian")
            : ("✅", "Selesai");

        var parts = new List<string> { $"{icon} **{label}**", $"{succeeded}/{planned.Count} tugas" };
        var lastBuild = verification?.Rounds.LastOrDefault(r => r.Build is not null)?.Build;
        if (lastBuild is not null) parts.Add(BuildLabel(lastBuild));
        var lastReview = verification?.Rounds.LastOrDefault(r => r.Review is not null)?.Review;
        if (lastReview is not null) parts.Add(ReviewLabel(lastReview));

        var builder = new StringBuilder(string.Join(" · ", parts)).Append("\n\n");
        var attention = Attention(execution, verificationFailed ? verification!.Rounds.LastOrDefault() : null);
        if (attention.Count > 0)
        {
            builder.Append("**Perlu perhatian**\n");
            foreach (var line in attention) builder.Append("- ").Append(line).Append('\n');
            builder.Append('\n');
        }
        return builder.ToString();
    }

    // Markdown table of the files the succeeded tasks changed, one row per file, with the tasks that touched it.
    // Empty when nothing changed.
    public static string ChangedFiles(ExecutionSummary execution)
    {
        var rows = new List<FileRow>();
        foreach (var result in execution.Results.Where(r => r.Status == ThreadTaskStatus.Succeeded))
        {
            var sections = string.IsNullOrWhiteSpace(result.Diff) ? [] : ParseDiff(result.Diff);
            if (sections.Count == 0) sections = [new DiffSection(result.File, result.File, 0, 0, Truncated: false, Counted: false)];
            foreach (var section in sections)
            {
                var path = section.To ?? section.From!;
                var row = rows.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new FileRow(path);
                    rows.Add(row);
                }
                row.Add(section, result.TaskKey);
            }
        }
        if (rows.Count == 0) return string.Empty;

        var builder = new StringBuilder("\n\n**File berubah**\n\n| File | Aksi | Baris | Tugas |\n|---|---|---|---|\n");
        foreach (var row in rows)
            builder.Append($"| {Code(row.Display)} | {row.Action} | {row.Lines} | {string.Join(", ", row.Tasks)} |\n");
        // The footer adds its own blank line.
        return builder.ToString().TrimEnd('\n');
    }

    // memoriesUsed: read by the planner. extraction: null when no extraction ran; pending when it did not finish in time.
    // quotas: the 9router accounts behind the nodes' models; now: the moment reset times are counted from.
    public static string Footer(
        TimeSpan elapsed,
        int tokens,
        int memoriesUsed,
        MemoryExtraction? extraction,
        bool extractionPending,
        int? wikiIntervalHours,
        string? dashboardUrl,
        Guid threadId,
        IReadOnlyList<RouterQuotaDto>? quotas = null,
        DateTime? now = null)
    {
        // Two spaces: terminals render ⏱️ and 🪙 as one cell but draw them two cells wide.
        var parts = new List<string> { $"⏱️  {Duration(elapsed)}", $"🪙  {Tokens(tokens)} token" };

        var memory = new List<string>();
        if (memoriesUsed > 0) memory.Add($"{memoriesUsed} memory dipakai");
        if (extractionPending) memory.Add("memory baru masih diproses");
        else if (extraction is { Failed: true }) memory.Add("memory gagal diekstrak");
        else if (extraction is not null) memory.Add(extraction.Saved > 0 ? $"+{extraction.Saved} memory baru" : "tidak ada memory baru");
        if (memory.Count > 0) parts.Add("🧠 " + string.Join(", ", memory));

        if (wikiIntervalHours is { } hours) parts.Add($"📚 Wiki masuk antrian rangkuman berikutnya (tiap {hours} jam)");

        var builder = new StringBuilder("\n\n").Append(string.Join(" · ", parts)).Append('\n');
        if (quotas is { Count: > 0 }) builder.Append('\n').Append(Quotas(quotas, now ?? DateTime.UtcNow));
        if (!string.IsNullOrWhiteSpace(dashboardUrl))
            builder.Append($"\n🔗 Detail & rollback: {dashboardUrl.TrimEnd('/')}/Thread/Detail/{threadId}\n");
        return builder.ToString();
    }

    // "📊 Sisa kuota", then per account its limits as bars, the bar first so they line up:
    //   - **Claude Code · Andi**
    //     - 🟩🟩🟩🟩🟩🟩🟩🟩🟩⬜ 95% · session (5h) · reset 4j 53m
    public static string Quotas(IReadOnlyList<RouterQuotaDto> quotas, DateTime now)
    {
        var builder = new StringBuilder("📊 **Sisa kuota**\n");
        foreach (var account in quotas.OrderBy(q => q.Plan, StringComparer.OrdinalIgnoreCase).ThenBy(q => q.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = string.Equals(account.Plan, account.Name, StringComparison.OrdinalIgnoreCase) ? account.Plan : $"{account.Plan} · {account.Name}";
            builder.Append("- **").Append(label).Append("**\n");
            foreach (var window in account.Windows)
                builder.Append("  - ").Append(QuotaWindow(window, now)).Append('\n');
        }
        return builder.ToString();
    }

    private static string QuotaWindow(RouterQuotaWindowDto window, DateTime now)
    {
        var amount = window.Unlimited ? "tanpa batas"
            : window.RemainingPercent switch
            {
                null => "?",
                <= 0 => "0% habis",
                < 1 => "<1%",
                double p => $"{Math.Round(p, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}%"
            };
        var bar = window.Unlimited ? QuotaBar(100) : window.RemainingPercent is { } percent ? QuotaBar(percent) : null;
        var text = (bar is null ? string.Empty : bar + " ") + $"{amount} · {window.Name}";
        if (window.ResetAt is { } reset && reset > now) text += $" · reset {Remaining(reset - now)}";
        return text;
    }

    // Green above 50%, yellow above 35%, red above 0, all empty when used up. Any remainder fills at least one cell.
    public static string QuotaBar(double remainingPercent)
    {
        var percent = Math.Clamp(remainingPercent, 0, 100);
        var cell = percent > 50 ? "🟩" : percent > 35 ? "🟨" : "🟥";
        var filled = percent <= 0 ? 0 : Math.Clamp((int)Math.Floor(percent / (100.0 / QuotaBarCells)), 1, QuotaBarCells);
        return string.Concat(Enumerable.Repeat(cell, filled)) + string.Concat(Enumerable.Repeat(QuotaEmptyCell, QuotaBarCells - filled));
    }

    // "45m", "3j 05m", "4h 19j" (menit, jam, hari).
    private static string Remaining(TimeSpan left) =>
        left.TotalHours < 1 ? $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))}m"
        : left.TotalDays < 1 ? $"{(int)left.TotalHours}j {left.Minutes:00}m"
        : $"{(int)left.TotalDays}h {left.Hours}j";

    public static string Duration(TimeSpan elapsed) => elapsed.TotalSeconds < 60
        ? $"{elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s"
        : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:00}s";

    public static string Tokens(int tokens) => tokens switch
    {
        >= 1_000_000 => $"{(tokens / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture)}M",
        >= 1_000 => $"{(tokens / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture)}k",
        _ => tokens.ToString(CultureInfo.InvariantCulture)
    };

    public static bool IsFixTask(string taskKey) => FixTaskId().IsMatch(taskKey);

    private static string BuildLabel(BuildRoundReport build) => build.Status switch
    {
        "passed" => "build lolos",
        "locked" => "build lolos (output terkunci)",
        "timeout" => "build melewati batas waktu",
        "error" => "build tidak bisa dijalankan",
        _ => $"build gagal ({build.ErrorCount} error)"
    };

    private static string ReviewLabel(ReviewRoundReport review) =>
        review.Error is not null ? "reviewer tidak memberi hasil"
        : review.Issues.Count == 0 ? "reviewer setuju"
        : $"reviewer: {review.Issues.Count} masalah";

    private static List<string> Attention(ExecutionSummary execution, VerificationRound? failedRound)
    {
        var lines = new List<string>();
        foreach (var result in execution.Results.Where(r => r.Status != ThreadTaskStatus.Succeeded))
        {
            var icon = result.Status == ThreadTaskStatus.Skipped ? "⏩" : "❌";
            var reason = OneLine(result.Error ?? (result.Status == ThreadTaskStatus.Skipped ? "dilewati" : "gagal"));
            lines.Add($"{icon} **{result.TaskKey}** {Code(result.File)}: {reason}");
        }

        if (failedRound?.Build is { Status: "failed" } build)
            lines.AddRange(build.Errors.Take(MaxAttentionErrors).Select(e => $"🔨 {Code(OneLine(e))}"));
        if (failedRound?.Review is { Error: null } review)
            lines.AddRange(review.Issues.Select(i => $"🔎 {Code(i.File)}: {OneLine(i.Problem)}"));
        return lines;
    }

    // Splits a task's diff into one section per file ("--- a/x" / "+++ b/y" pairs); null is /dev/null.
    private static List<DiffSection> ParseDiff(string diff)
    {
        var sections = new List<DiffSection>();
        var lines = diff.Replace("\r", string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("--- ", StringComparison.Ordinal) || i + 1 >= lines.Length || !lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
                continue;

            var from = DiffPath(lines[i][4..]);
            var to = DiffPath(lines[i + 1][4..]);
            int added = 0, removed = 0;
            var truncated = false;
            var j = i + 2;
            for (; j < lines.Length; j++)
            {
                var line = lines[j];
                if (line.StartsWith("--- ", StringComparison.Ordinal) && j + 1 < lines.Length && lines[j + 1].StartsWith("+++ ", StringComparison.Ordinal)) break;
                if (line.StartsWith("... (", StringComparison.Ordinal)) truncated = true;
                else if (line.StartsWith('+')) added++;
                else if (line.StartsWith('-')) removed++;
            }
            if (from is not null || to is not null) sections.Add(new DiffSection(from, to, added, removed, truncated, Counted: true));
            i = j - 1;
        }
        return sections;
    }

    private static string? DiffPath(string header)
    {
        var path = header.Trim();
        if (path == "/dev/null") return null;
        return path.StartsWith("a/", StringComparison.Ordinal) || path.StartsWith("b/", StringComparison.Ordinal) ? path[2..] : path;
    }

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    // Inline code that survives backticks and table pipes in the text.
    private static string Code(string text) => $"`{text.Replace('`', '\'').Replace("|", "\\|")}`";

    [GeneratedRegex(@"^V\d+-\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex FixTaskId();

    private sealed record DiffSection(string? From, string? To, int Added, int Removed, bool Truncated, bool Counted);

    private sealed class FileRow(string path)
    {
        private bool _created;
        private bool _deleted;
        private string? _movedFrom;
        private int _added;
        private int _removed;
        private bool _truncated;
        private bool _counted;

        public string Path { get; } = path;
        public List<string> Tasks { get; } = [];

        public string Display => _movedFrom is null ? Path : $"{_movedFrom} → {Path}";

        // A file the thread created stays "baru" whatever edited it later; a delete is the last word.
        public string Action => _deleted ? "hapus" : _created ? "baru" : _movedFrom is not null ? "pindah" : "ubah";

        public string Lines
        {
            get
            {
                if (_deleted || !_counted || _added + _removed == 0) return "—";
                var parts = new List<string>();
                if (_added > 0) parts.Add($"+{_added}");
                if (_removed > 0) parts.Add($"−{_removed}");
                return string.Join(" ", parts) + (_truncated ? "…" : string.Empty);
            }
        }

        public void Add(DiffSection section, string taskKey)
        {
            if (!Tasks.Contains(taskKey, StringComparer.OrdinalIgnoreCase)) Tasks.Add(taskKey);
            if (section.From is null) _created = true;
            if (section.To is null) _deleted = true;
            if (section.From is not null && section.To is not null && !string.Equals(section.From, section.To, StringComparison.OrdinalIgnoreCase) && !_created)
                _movedFrom ??= section.From;
            _added += section.Added;
            _removed += section.Removed;
            _truncated |= section.Truncated;
            _counted |= section.Counted;
        }
    }
}
