using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

// The parts of the final answer AIT writes itself around the wrapper's text, from the results and never from a model:
// a status line with what still needs attention, the table of changed files, the test with its API output, and the
// footer (time, tokens, memory, wiki, remaining provider quota, dashboard link).
public static partial class FinalReport
{
    private const int MaxAttentionErrors = 10;

    // API output in the answer: the first requests, each body cut to a readable size; the thread page keeps more.
    private const int MaxReportedRequests = 5;
    private const int MaxReportedBodyLines = 30;
    private const int MaxReportedBodyChars = 2_000;
    private const int MaxReportedBrowserErrors = 5;

    // Unit tests in the answer: the ones that need a word (failed, fixed, skipped, ...), each detail cut short.
    private const int MaxReportedUnitTests = 10;
    private const int MaxUnitTestDetailChars = 200;

    // Quota bars: one cell per 10%, coloured by what remains (terminals only colour emoji).
    private const int QuotaBarCells = 10;
    private const string QuotaEmptyCell = "⬜";

    // "✅ **Selesai** · 8/8 tugas · build lolos · reviewer setuju · unit test lolos (12) · testing lolos", then "Perlu
    // perhatian" when something failed. Fix tasks (V<round>-<n>) do not count as tasks of the plan; their failures are
    // listed like the others. A failed test makes the thread "Selesai sebagian": the change does not work yet.
    // retryHint (chat threads) tells how to run planned tasks that did not succeed again with "ulangi" (RetryReply).
    public static string Status(ExecutionSummary execution, VerificationReport? verification, TestingReport? testing = null, UnitTestReport? unitTests = null,
        bool retryHint = false)
    {
        var planned = execution.Results.Where(r => !IsFixTask(r.TaskKey)).ToList();
        var succeeded = planned.Count(r => r.Status == ThreadTaskStatus.Succeeded);
        var verificationFailed = verification?.Outcome == VerificationReport.Failed;
        var (icon, label) = execution.Results.All(r => r.Status != ThreadTaskStatus.Succeeded) ? ("❌", Lang.T("Gagal", "Failed"))
            : succeeded < planned.Count || verificationFailed || testing?.Outcome == TestingReport.Failed || unitTests?.Outcome == UnitTestReport.Failed ? ("🟡", Lang.T("Selesai sebagian", "Partly completed"))
            : ("✅", Lang.T("Selesai", "Completed"));

        var parts = new List<string> { $"{icon} **{label}**", Lang.T($"{succeeded}/{planned.Count} tugas", $"{succeeded}/{planned.Count} tasks") };
        var lastBuild = verification?.Rounds.LastOrDefault(r => r.Build is not null)?.Build;
        if (lastBuild is not null) parts.Add(BuildLabel(lastBuild));
        var lastReview = verification?.Rounds.LastOrDefault(r => r.Review is not null)?.Review;
        if (lastReview is not null) parts.Add(ReviewLabel(lastReview));
        if (unitTests is { Tests.Count: > 0 }) parts.Add(UnitTestLabel(unitTests));
        if (testing is not null) parts.Add(TestingLabel(testing));

        var builder = new StringBuilder(string.Join(" · ", parts)).Append("\n\n");
        var attention = Attention(execution, verificationFailed ? verification!.Rounds.LastOrDefault() : null);
        if (unitTests is not null)
            attention.AddRange(unitTests.Tests.Where(t => t.IsProblem).Select(t => Lang.T($"🧪 **{t.Number}** {OneLine(t.Title)}: {OneLine(t.Message ?? "gagal")}", $"🧪 **{t.Number}** {OneLine(t.Title)}: {OneLine(t.Message ?? "failed")}")));
        if (attention.Count > 0)
        {
            builder.Append(Lang.T("**Perlu perhatian**\n", "**Needs attention**\n"));
            foreach (var line in attention) builder.Append("- ").Append(line).Append('\n');
            builder.Append('\n');
        }
        if (retryHint && succeeded < planned.Count)
            builder.Append(Lang.T("🔁 Balas `ulangi` untuk menjalankan lagi tugas yang belum berhasil dengan rencana yang sama (planner tidak dipanggil).\n\n", "🔁 Reply `retry` to run the tasks that did not succeed again with the same plan (the planner is not called).\n\n"));
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

        var builder = new StringBuilder(Lang.T("\n\n**File berubah**\n\n| File | Aksi | Baris | Tugas |\n|---|---|---|---|\n", "\n\n**Changed files**\n\n| File | Action | Lines | Task |\n|---|---|---|---|\n"));
        foreach (var row in rows)
            builder.Append($"| {Code(row.Display)} | {row.Action} | {row.Lines} | {string.Join(", ", row.Tasks)} |\n");
        // The footer adds its own blank line.
        return builder.ToString().TrimEnd('\n');
    }

    // The tester's verdict per scenario, what kept it from running, the browser errors it saw, and the API responses:
    //   **🧪 Testing** · API · `http://localhost:5000`
    //   - ✅ **Tambah user** — tersimpan, status 201
    //   **Output API**
    //   `POST /api/users` → **201 Created** · 35 ms  + the body in a code block
    // The tester's suggestions, with the result of the ones already run:
    //   **🧭 Test lanjutan yang disarankan**
    //   1. **Approve level 1 oleh atasan** — status harus Menunggu HR · `atasan01` · ✅ lolos
    // and how to run one. Null when there are none.
    public static string? FollowUps(TestingReport report)
    {
        if (report.FollowUps.Count == 0) return null;
        var builder = new StringBuilder(Lang.T("\n**🧭 Test lanjutan yang disarankan**\n\n", "\n**🧭 Suggested follow-up tests**\n\n"));
        foreach (var followUp in report.FollowUps)
        {
            builder.Append($"{followUp.Number}. **{OneLine(followUp.Title)}**");
            if (followUp.Why is not null) builder.Append(" — ").Append(OneLine(followUp.Why));
            var actors = followUp.Scenarios.Select(s => s.Actor).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (actors.Count > 0) builder.Append(" · ").Append(string.Join(", ", actors.Select(Code)));
            if (followUp.LastRun is { } run) builder.Append(" · ").Append(TestingLabel(run));
            builder.Append('\n');
            if (followUp.Needs is not null) builder.Append(Lang.T("   Perlu: ", "   Needs: ")).Append(OneLine(followUp.Needs)).Append('\n');
        }
        builder.Append(Lang.T($"\nJalankan dengan membalas `jalankan test lanjutan <nomor>` (mis. `jalankan test lanjutan {report.FollowUps[0].Number}`), ", $"\nRun one by replying `run follow-up test <number>` (e.g. `run follow-up test {report.FollowUps[0].Number}`), ") +
                       Lang.T("atau tombol di halaman thread. Untuk menentukan sendiri akun tiap peran dan nilai form, balas ", "or with the button on the thread page. To choose the account of each role and the form values yourself, reply ") +
                       Lang.T($"`buat skenario test lanjutan {report.FollowUps[0].Number}`: AIT membuat file Excel di `.aithread/testing/` untuk diisi. ", $"`create scenario for follow-up test {report.FollowUps[0].Number}`: AIT creates an Excel file in `.aithread/testing/` to fill in. ") +
                       Lang.T("Test lanjutan membuat data sungguhan seperti testing biasa.\n", "Follow-up tests create real data like regular testing.\n"));
        return builder.ToString();
    }

    public static string Testing(TestingReport report)
    {
        var builder = new StringBuilder("\n\n**🧪 Testing**");
        if (report.Outcome == TestingReport.Skipped)
            return builder.Append(Lang.T(" dilewati", " skipped")).Append(report.Summary is null ? string.Empty : $" — {OneLine(report.Summary)}").ToString();

        builder.Append(report.Level == TestPlan.Ui ? " · UI" : " · API");
        if (report.BaseUrl is not null) builder.Append(" · ").Append(Code(report.BaseUrl));
        if (report.Browser is not null) builder.Append(" · ").Append(report.Browser);
        if (report.LoginUser is not null) builder.Append(" · login ").Append(Code(report.LoginUser));
        if (report.Accounts.Count > 0) builder.Append(Lang.T(" · akun ", " · accounts ")).Append(string.Join(", ", report.Accounts.Select(Code)));
        if (report.AppStarted is not null) builder.Append(Lang.T(" · dijalankan AIT ", " · started by AIT ")).Append(Code(report.AppStarted));
        builder.Append('\n');
        if (report.DefaultDevPassword)
            builder.Append(Lang.T($"\nℹ️ Login akun uji memakai password dev bawaan {Code(TestingSettings.DefaultDevPassword)}. Bila aplikasi memakai password lain di dev, isi di Proyek > Detail → Akun uji.\n", $"\nℹ️ Test accounts log in with the default dev password {Code(TestingSettings.DefaultDevPassword)}. If the application uses another password in dev, set it in Project > Detail → Test accounts.\n"));
        if (report.Error is not null)
        {
            // The URL the sentence starts with and the command AIT tried as code; the command holds the URL too, so only
            // the first occurrence of the URL is replaced.
            var error = OneLine(report.Error);
            var at = report.BaseUrl is null ? -1 : error.IndexOf(report.BaseUrl, StringComparison.Ordinal);
            if (at >= 0) error = error[..at] + Code(report.BaseUrl!) + error[(at + report.BaseUrl!.Length)..];
            if (report.AppCommand is not null) error = error.Replace(report.AppCommand, Code(report.AppCommand));
            builder.Append("\n⚠️ ").Append(error).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(report.AppOutput)) builder.Append(Lang.T("\n**Output aplikasi**\n", "\n**Application output**\n")).Append(CodeBlock(report.AppOutput, "text"));
        if (report.Summary is not null) builder.Append('\n').Append(OneLine(report.Summary)).Append('\n');

        if (report.Scenarios.Count > 0)
        {
            builder.Append('\n');
            foreach (var scenario in report.Scenarios)
            {
                var icon = scenario.Status switch { TestingReport.Passed => "✅", TestingReport.Failed => "❌", _ => "🚫" };
                builder.Append($"- {icon} **{OneLine(scenario.Title)}**");
                if (scenario.Actor is not null) builder.Append(" (").Append(Code(scenario.Actor)).Append(')');
                // A scenario that could not run because of the whole test says so in the line above already.
                if (!string.IsNullOrWhiteSpace(scenario.Details) && scenario.Details != report.Error) builder.Append(" — ").Append(OneLine(scenario.Details));
                builder.Append('\n');
            }
        }

        if (report.BrowserErrors.Count > 0)
        {
            builder.Append(Lang.T("\n**Error di browser**\n", "\n**Browser errors**\n"));
            foreach (var error in report.BrowserErrors.Take(MaxReportedBrowserErrors)) builder.Append("- ").Append(Code(error)).Append('\n');
            if (report.BrowserErrors.Count > MaxReportedBrowserErrors) builder.Append(Lang.T($"- … {report.BrowserErrors.Count - MaxReportedBrowserErrors} lainnya\n", $"- … {report.BrowserErrors.Count - MaxReportedBrowserErrors} more\n"));
        }

        if (report.Requests.Count > 0)
        {
            builder.Append("\n**Output API**\n");
            foreach (var request in report.Requests.Take(MaxReportedRequests))
            {
                var path = Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ? uri.PathAndQuery : request.Url;
                builder.Append('\n').Append(Code($"{request.Method} {path}")).Append(" → ");
                if (request.Error is not null)
                {
                    builder.Append("❌ ").Append(OneLine(request.Error)).Append('\n');
                    continue;
                }
                builder.Append($"**{request.Status} {request.Reason}** · {request.DurationMs} ms");
                if (request.Location is not null) builder.Append(" · Location ").Append(Code(request.Location));
                builder.Append('\n');
                if (!string.IsNullOrWhiteSpace(request.ResponseBody))
                    builder.Append(CodeBlock(request.ResponseBody, request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true ? "json" : "text"));
            }
            if (report.Requests.Count > MaxReportedRequests)
                builder.Append(Lang.T($"\n… {report.Requests.Count - MaxReportedRequests} request lain ada di halaman thread.\n", $"\n… {report.Requests.Count - MaxReportedRequests} more requests are on the thread page.\n"));
        }

        if (FollowUps(report) is { } followUps) builder.Append(followUps);

        // Last, so the question is what the developer reads at the end of the answer.
        if (report.LoginNeeded is not null)
        {
            builder.Append(Lang.T("\n🔐 **Tester butuh login** — ", "\n🔐 **Tester needs a login** — ")).Append(OneLine(report.LoginNeeded));
            if (report.LoginUrl is not null) builder.Append(" (").Append(Code(report.LoginUrl)).Append(')');
            builder.Append("\n\n").Append(report.AskedLogin && report.LoginKind == TestingReport.LoginKindDevPassword
                ? Lang.T("Password dev project ditolak aplikasi. Balas pesan ini dengan `password dev <nilai>`: AIT menyimpannya sebagai password dev project ", "The application rejected the project dev password. Reply to this message with `dev password <value>`: AIT stores it as the project dev password ") +
                  Lang.T("(terlihat oleh semua developer project, jadi hanya untuk password bypass di dev) lalu menjalankan testing lagi. ", "(visible to every developer of the project, so only for a bypass password in dev) and runs the testing again. ") +
                  Lang.T("Untuk password asli, balas `login <username> <password>` (hanya untuk sesi ini, tidak disimpan).\n", "For a real password, reply `login <username> <password>` (this session only, not stored).\n")
                : report.AskedLogin
                ? Lang.T("Balas pesan ini dengan `login <username> <password>` (akun uji) untuk menjalankan testing lagi dengan akun itu. ", "Reply to this message with `login <username> <password>` (a test account) to run the testing again with that account. ") +
                  Lang.T("AIT tidak menyimpannya ke disk dan tidak mengirim password ke model (tester hanya melihat `{{password}}`); ", "AIT does not store it on disk and never sends the password to a model (the tester only sees `{{password}}`); ") +
                  Lang.T("login diingat selama sesi chat ini agar tidak ditanya ulang. Password tetap tercatat di riwayat chat Anda, jadi pakai akun uji.\n", "the login is remembered for this chat session so you are not asked again. The password stays in your chat history, so use a test account.\n")
                : Lang.T("Jalankan lagi lewat **🧪 Ulangi testing** di detail thread dan isi username & password di sana.\n", "Run it again with **🧪 Test again** in the thread detail and fill in the username & password there.\n"));
        }
        // The footer adds its own blank line.
        return builder.ToString().TrimEnd('\n');
    }

    // A thread that only ran tests: "🧪 **Pengujian** · unit test: 1 gagal · testing lolos".
    public static string TestOnlyStatus(UnitTestReport? unitTests, TestingReport? testing)
    {
        var failed = unitTests?.Outcome == UnitTestReport.Failed || testing?.Outcome == TestingReport.Failed;
        var parts = new List<string> { failed ? Lang.T("🟡 **Pengujian selesai, ada yang gagal**", "🟡 **Testing finished, something failed**") : Lang.T("🧪 **Pengujian selesai**", "🧪 **Testing finished**"), Lang.T("tanpa perubahan kode", "no code change") };
        if (unitTests is not null) parts.Add(UnitTestLabel(unitTests));
        if (testing is not null) parts.Add(TestingLabel(testing));
        return string.Join(" · ", parts) + "\n\n";
    }

    // Every test the thread ran, with what changed: "❌ **UT-0012** Simpan tanpa nama ditolak — lolos → gagal: ...".
    // Tests that stayed passing are counted, not listed.
    public static string UnitTests(UnitTestReport report)
    {
        var builder = new StringBuilder("\n\n**🧪 Unit test**");
        if (report.Summary is not null) builder.Append(" · ").Append(OneLine(report.Summary));
        builder.Append('\n');
        var listed = report.Tests.Where(t => t.Verdict is not (UnitTestReportItem.Ok or UnitTestReportItem.Retired)).ToList();
        if (listed.Count > 0) builder.Append('\n');
        foreach (var test in listed.Take(MaxReportedUnitTests))
        {
            var icon = test.Verdict switch
            {
                UnitTestReportItem.Regression or UnitTestReportItem.NewFailing or UnitTestReportItem.Failing => "❌",
                UnitTestReportItem.Fixed => "✅",
                UnitTestReportItem.Preexisting => "🟠",
                UnitTestReportItem.Flaky => "〰️",
                _ => "⏭️"
            };
            builder.Append($"- {icon} **{test.Number}** {OneLine(test.Title)} — {UnitTestVerdictLabel(test)}");
            var detail = test.SkipReason ?? (test.Verdict is UnitTestReportItem.Fixed ? null : test.Message);
            if (!string.IsNullOrWhiteSpace(detail)) builder.Append(": ").Append(Code(Clip(OneLine(detail), MaxUnitTestDetailChars)));
            builder.Append('\n');
        }
        if (listed.Count > MaxReportedUnitTests) builder.Append(Lang.T($"- … {listed.Count - MaxReportedUnitTests} lainnya ada di halaman thread\n", $"- … {listed.Count - MaxReportedUnitTests} more are on the thread page\n"));
        foreach (var retired in report.Tests.Where(t => t.Verdict == UnitTestReportItem.Retired))
            builder.Append(Lang.T($"- 🗃️ **{retired.Number}** {OneLine(retired.Title)} — ditandai obsolete, menunggu konfirmasi hapus di Proyek > Detail\n", $"- 🗃️ **{retired.Number}** {OneLine(retired.Title)} — marked obsolete, waiting for the deletion to be confirmed in Project > Detail\n"));
        foreach (var restored in report.Restored.Take(MaxReportedUnitTests))
            builder.Append($"- ♻️ **{restored.Number}** `{restored.File}` — {UnitTestStage.RestoreLabel(restored)}\n");
        foreach (var note in report.Notes) builder.Append($"- ℹ️ {OneLine(note)}\n");
        // The footer adds its own blank line.
        return builder.ToString().TrimEnd('\n');
    }

    // "unit test lolos (12)", "unit test: 1 regresi", ... for the status line.
    public static string UnitTestLabel(UnitTestReport report)
    {
        var ran = report.Tests.Count(t => t.After is not null);
        var regressions = report.Tests.Count(t => t.Verdict == UnitTestReportItem.Regression);
        var failing = report.Tests.Count(t => t.Verdict is UnitTestReportItem.NewFailing or UnitTestReportItem.Failing);
        if (regressions > 0) return Lang.T($"unit test: {regressions} regresi", $"unit test: {regressions} regressions");
        if (failing > 0) return Lang.T($"unit test: {failing} gagal", $"unit tests: {failing} failed");
        return ran == 0 ? Lang.T("unit test dilewati", "unit tests skipped") : Lang.T($"unit test lolos ({ran})", $"unit tests passed ({ran})");
    }

    private static string UnitTestVerdictLabel(UnitTestReportItem test) => test.Verdict switch
    {
        UnitTestReportItem.Regression => Lang.T("lolos sebelum perubahan, sekarang gagal", "passed before the change, fails now"),
        UnitTestReportItem.NewFailing => Lang.T("test baru gagal", "new test fails"),
        UnitTestReportItem.Failing => Lang.T("gagal", "failed"),
        UnitTestReportItem.Fixed => Lang.T("gagal sebelum perubahan, sekarang lolos", "failed before the change, passes now"),
        UnitTestReportItem.Preexisting => Lang.T("sudah gagal sebelum perubahan (bukan karena perubahan ini)", "already failing before the change (not because of this change)"),
        UnitTestReportItem.Flaky => Lang.T("tidak stabil", "flaky"),
        UnitTestReportItem.NotFound => Lang.T("tidak dilaporkan runner", "not reported by the runner"),
        UnitTestReportItem.Missing => Lang.T("test baru tidak ditemukan di file-nya", "new test not found in its file"),
        _ => Lang.T("dilewati", "skipped")
    };

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    // "testing lolos", "testing: 1 gagal", ... for the status line.
    public static string TestingLabel(TestingReport report) => report.LoginNeeded is not null ? Lang.T("testing menunggu login", "testing waits for a login") : report.Outcome switch
    {
        TestingReport.Passed => report.Count(TestingReport.Blocked) == 0 ? Lang.T("testing lolos", "testing passed") : Lang.T($"testing lolos ({report.Count(TestingReport.Blocked)} tidak bisa dicek)", $"testing passed ({report.Count(TestingReport.Blocked)} could not be checked)"),
        TestingReport.Failed => Lang.T($"testing: {report.Count(TestingReport.Failed)} gagal", $"testing: {report.Count(TestingReport.Failed)} failed"),
        TestingReport.Skipped => Lang.T("testing dilewati", "testing skipped"),
        _ => Lang.T("testing tidak bisa dijalankan", "testing could not run")
    };

    // A body in a fenced block, cut to MaxReportedBodyLines/Chars; the fence is longer than any backtick run inside.
    private static string CodeBlock(string body, string language)
    {
        var lines = body.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
        var text = string.Join('\n', lines.Take(MaxReportedBodyLines));
        var cut = lines.Length > MaxReportedBodyLines || text.Length > MaxReportedBodyChars;
        if (text.Length > MaxReportedBodyChars) text = text[..MaxReportedBodyChars];
        if (cut) text += "\n…";
        var longest = 0;
        for (int i = 0, run = 0; i < text.Length; i++)
        {
            run = text[i] == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}{language}\n{text}\n{fence}\n";
    }

    // memoriesUsed: read by the planner. extraction: null when no extraction ran; pending when it did not finish in time.
    // quotas: the 9router accounts behind the nodes' models; now: the moment reset times are counted from.
    // secretsMasked: distinct secrets replaced by placeholders before they reached a model (SecretGuard).
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
        DateTime? now = null,
        int secretsMasked = 0)
    {
        // Two spaces: terminals render ⏱️ and 🪙 as one cell but draw them two cells wide.
        var parts = new List<string> { $"⏱️  {Duration(elapsed)}", $"🪙  {Tokens(tokens)} token" };
        if (secretsMasked > 0) parts.Add(Lang.T($"🔒 {secretsMasked} secret disamarkan", $"🔒 {secretsMasked} secret(s) masked"));

        var memory = new List<string>();
        if (memoriesUsed > 0) memory.Add(Lang.T($"{memoriesUsed} memory dipakai", $"{memoriesUsed} memories used"));
        if (extractionPending) memory.Add(Lang.T("memory baru masih diproses", "new memory still being processed"));
        else if (extraction is { Failed: true }) memory.Add(Lang.T("memory gagal diekstrak", "memory extraction failed"));
        else if (extraction is not null) memory.Add(extraction.Saved > 0 ? Lang.T($"+{extraction.Saved} memory baru", $"+{extraction.Saved} new memory") : Lang.T("tidak ada memory baru", "no new memory"));
        if (memory.Count > 0) parts.Add("🧠 " + string.Join(", ", memory));

        if (wikiIntervalHours is { } hours) parts.Add(Lang.T($"📚 Wiki masuk antrian rangkuman berikutnya (tiap {hours} jam)", $"📚 Wiki queued for the next summary (every {hours} h)"));

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
        var builder = new StringBuilder(Lang.T("📊 **Sisa kuota**\n", "📊 **Remaining quota**\n"));
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
        var amount = window.Unlimited ? Lang.T("tanpa batas", "unlimited")
            : window.RemainingPercent switch
            {
                null => "?",
                <= 0 => Lang.T("0% habis", "0% left"),
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
        "passed" => Lang.T("build lolos", "build passed"),
        "locked" => Lang.T("build lolos (output terkunci)", "build passed (output locked)"),
        "timeout" => Lang.T("build melewati batas waktu", "build timed out"),
        "error" => Lang.T("build tidak bisa dijalankan", "build could not run"),
        _ => Lang.T($"build gagal ({build.ErrorCount} error)", $"build failed ({build.ErrorCount} errors)")
    };

    private static string ReviewLabel(ReviewRoundReport review) =>
        review.Error is not null ? Lang.T("reviewer tidak memberi hasil", "the reviewer gave no result")
        : review.Issues.Count == 0 ? Lang.T("reviewer setuju", "reviewer approved")
        : Lang.T($"reviewer: {review.Issues.Count} masalah", $"reviewer: {review.Issues.Count} issues");

    private static List<string> Attention(ExecutionSummary execution, VerificationRound? failedRound)
    {
        var lines = new List<string>();
        foreach (var result in execution.Results.Where(r => r.Status != ThreadTaskStatus.Succeeded))
        {
            var icon = result.Status == ThreadTaskStatus.Skipped ? "⏩" : "❌";
            var reason = OneLine(result.Error ?? (result.Status == ThreadTaskStatus.Skipped ? Lang.T("dilewati", "skipped") : Lang.T("gagal", "failed")));
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
        public string Action => _deleted ? Lang.T("hapus", "deleted") : _created ? Lang.T("baru", "new") : _movedFrom is not null ? Lang.T("pindah", "moved") : Lang.T("ubah", "changed");

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
