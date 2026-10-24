using System.Globalization;
using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using static AIThread.Service.Telegram.TelegramFormatter;

namespace AIThread.Service.Telegram;

public sealed record TelegramProject(Guid Id, string Name, string RootPath);

// Everything the bot writes (Indonesian, Telegram HTML), plus the command and button formats it reads back.
// Lines are joined with "\n" explicitly: Telegram shows "\r" literally.
public static class TelegramMessages
{
    public static readonly IReadOnlyList<TelegramCommand> Commands =
    [
        new("projects", "Pilih project untuk prompt"),
        new("status", "Thread berjalan & terakhir"),
        new("cancel", "Batalkan thread yang berjalan"),
        new("diff", "Perubahan file thread terakhir"),
        new("new", "Mulai percakapan baru"),
        new("help", "Bantuan")
    ];

    // Inline button payloads: prefix + Guid "N" (34 bytes, within Telegram's 64-byte limit).
    public const string ProjectButton = "p:";
    public const string CancelButton = "c:";

    public const string TextOnly = "Hanya pesan teks yang didukung. Kirim /help untuk bantuan.";
    public const string UnknownCommand = "Perintah tidak dikenal. Kirim /help untuk daftar perintah.";
    public const string NoProjects = "Belum ada project terdaftar. Buka sesi di folder project (berisi <code>.git</code> atau file build) atau daftarkan foldernya di menu <b>Project</b> AIT terlebih dahulu.";
    public const string ConversationReset = "🆕 Percakapan baru. Prompt berikutnya dikirim tanpa riwayat sebelumnya.";
    public const string NothingToCancel = "Tidak ada thread berjalan dari chat ini. Untuk thread lain: <code>/cancel &lt;id&gt;</code> (lihat /status).";
    public const string NoThreadYet = "Belum ada thread dari chat ini.";
    public const string Revoked = "🔒 Akses chat ini ke AIT dicabut.";
    public const string DiffTruncated = "… diff terlalu panjang untuk Telegram; lihat detail thread di dashboard AIT.";

    private const int MaxListedTasks = 25;
    private const int MaxListedProjects = 30;
    private const int MaxStatusLength = 3800;

    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");

    // "/project@MyBot 2" → ("project", "2").
    public static (string Command, string Argument) ParseCommand(string text)
    {
        var trimmed = text.Trim();
        var end = trimmed.IndexOfAny([' ', '\n', '\t']);
        var head = end < 0 ? trimmed : trimmed[..end];
        var argument = end < 0 ? string.Empty : trimmed[(end + 1)..].Trim();

        var command = head.TrimStart('/');
        var at = command.IndexOf('@');
        if (at >= 0) command = command[..at];
        return (command.ToLowerInvariant(), argument);
    }

    public static string ButtonData(string prefix, Guid id) => prefix + id.ToString("N");

    public static bool TryParseButton(string? data, string prefix, out Guid id)
    {
        id = Guid.Empty;
        return data is not null && data.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(data[prefix.Length..], "N", out id);
    }

    public static bool IsActive(ThreadStatus status) =>
        status is ThreadStatus.Pending or ThreadStatus.Planning or ThreadStatus.Executing or ThreadStatus.Verifying or ThreadStatus.Testing
            or ThreadStatus.Wrapping;

    public static string Help(string? activeProject) => Lines(
        "🧵 <b>AIT</b> — prompt jarak jauh",
        "",
        "Kirim pesan biasa untuk menjalankan prompt di project aktif. Planner menyusun rencana, executor mengerjakan paralel, lalu ringkasannya dikirim ke sini.",
        "",
        ActiveProjectLine(activeProject),
        "",
        "/projects — pilih project",
        "/status — thread berjalan &amp; terakhir",
        "/cancel — batalkan thread yang berjalan",
        "/diff — perubahan file thread terakhir",
        "/new — mulai percakapan baru",
        "/help — bantuan ini",
        "",
        "<i>Prompt lanjutan di project yang sama membawa percakapan sebelumnya sebagai konteks, sampai /new atau ganti project.</i>");

    public static string NotAllowed(string displayName, long chatId) => Lines(
        "🔒 Chat ini belum diizinkan memakai AIT.",
        "",
        $"Di komputer yang menjalankan AIT, buka <b>Setting → Telegram</b> lalu klik <b>Izinkan</b> pada chat <b>{Escape(displayName)}</b> (ID <code>{chatId}</code>).");

    public static string Allowed(string? activeProject) => Lines("✅ Chat ini sekarang diizinkan memakai AIT.", "", Help(activeProject));

    public static string Stale(DateTime sentAtUtc) =>
        $"⏸️ Pesan ini terkirim saat AIT tidak berjalan ({sentAtUtc.ToLocalTime().ToString("d MMM HH:mm", Indonesian)}) sehingga tidak dijalankan. Kirim ulang bila masih diperlukan.";

    public static string Starting(string projectName) => $"🧵 <b>{Escape(projectName)}</b> · menyiapkan thread…";

    public static string StartFailed(string error) => $"❌ Thread tidak dapat dimulai: {Escape(error)}";

    public static string StillRunning(Guid threadId) =>
        $"⏳ Thread <code>{ShortId(threadId)}</code> masih berjalan. Tunggu sampai selesai atau kirim /cancel.";

    public static string Failure(string error) => $"⚠️ Terjadi kesalahan di AIT: {Escape(error)}";

    public static string Interrupted(Guid threadId) =>
        $"⚠️ AIT berhenti saat thread <code>{ShortId(threadId)}</code> berjalan, jadi thread ini tidak selesai. " +
        $"Tugas yang sempat selesai tetap tertulis — cek <code>/diff {ShortId(threadId)}</code> setelah AIT hidup lagi, lalu kirim ulang prompt bila perlu.";

    public static string ProjectList(IReadOnlyList<TelegramProject> projects, Guid? activeId, string? intro)
    {
        var builder = new StringBuilder(intro ?? "📁 <b>Pilih project</b> untuk prompt berikutnya:").Append("\n\n");
        for (var i = 0; i < projects.Count && i < MaxListedProjects; i++)
        {
            var p = projects[i];
            builder.Append(i + 1).Append(". ").Append(p.Id == activeId ? "✅ " : string.Empty)
                .Append("<b>").Append(Escape(p.Name)).Append("</b> — <code>").Append(Escape(ShortPath(p.RootPath))).Append("</code>\n");
        }
        if (projects.Count > MaxListedProjects)
            builder.Append($"… dan {projects.Count - MaxListedProjects} project lain — ketik <code>/project &lt;nama&gt;</code>\n");
        return builder.ToString().TrimEnd();
    }

    public static TgInlineKeyboard ProjectKeyboard(IReadOnlyList<TelegramProject> projects, Guid? activeId) =>
        new(projects.Take(MaxListedProjects)
            .Select(p => (IReadOnlyList<TgInlineButton>)[new TgInlineButton((p.Id == activeId ? "✅ " : string.Empty) + Truncate(p.Name, 40), ButtonData(ProjectButton, p.Id))])
            .ToList());

    public static string ProjectNotFound(string argument) => $"Project \"{Escape(argument)}\" tidak ditemukan. Pilih dari daftar:";

    public static string ProjectSelected(string name, bool runningPendingPrompt) => runningPendingPrompt
        ? $"📁 Project aktif: <b>{Escape(name)}</b>. Menjalankan prompt Anda…"
        : $"📁 Project aktif: <b>{Escape(name)}</b>. Kirim prompt Anda.";

    public static string PickProjectForPrompt => "📁 Pilih project untuk prompt ini — prompt langsung dijalankan setelah Anda memilih:";

    public static TgInlineKeyboard CancelKeyboard(Guid threadId) =>
        new([[new TgInlineButton("⛔ Batalkan", ButtonData(CancelButton, threadId))]]);

    public static string CancelSent(Guid threadId) => $"⛔ Pembatalan dikirim ke thread <code>{ShortId(threadId)}</code>.";

    public static string NotRunning(Guid threadId) => $"Thread <code>{ShortId(threadId)}</code> sudah tidak berjalan.";

    public static string NoRunningMatch(string argument) => $"Tidak ada thread berjalan dengan id <code>{Escape(argument)}</code>.";

    public static string ThreadNotFound(string argument) => $"Thread <code>{Escape(argument)}</code> tidak ditemukan.";

    public static string NoChanges(Guid threadId) => $"Thread <code>{ShortId(threadId)}</code> tidak mengubah file.";

    public static string DiffHeader(Guid threadId, int files) => $"🧾 Perubahan thread <code>{ShortId(threadId)}</code> · {files} file";

    public static string DiffTask(ThreadTaskDto task) =>
        $"{TaskIcon(task.Status)} <b>{Escape(task.TaskKey)}</b> <code>{Escape(ShortPath(task.FilePath))}</code>" +
        (task.TargetPath is null ? string.Empty : $" → <code>{Escape(ShortPath(task.TargetPath))}</code>");

    // The live message of a running thread: status, plan, one line per task and the planner's latest tool call.
    public static string ThreadProgress(ThreadDetailDto detail, string projectName, string? plannerActivity, DateTime now)
    {
        var s = detail.Summary;
        var (icon, label) = StatusLabel(s);
        var header = new StringBuilder()
            .Append("🧵 <b>").Append(Escape(projectName)).Append("</b> · thread <code>").Append(ShortId(s.Id)).Append("</code>\n")
            .Append(icon).Append(" <b>").Append(label).Append("</b>");
        if (s.TaskCount > 0) header.Append($" · {s.TasksSucceeded}/{s.TaskCount} tugas");
        if (s.TasksFailed > 0) header.Append($" · {s.TasksFailed} gagal");
        header.Append(" · ").Append(Duration(s.TotalMs ?? (long)(now - s.CreatedAt).TotalMilliseconds));
        if (!string.IsNullOrWhiteSpace(s.PlanSummary)) header.Append("\n\n📋 ").Append(Escape(Truncate(s.PlanSummary, 300)));

        var footer = plannerActivity is not null && s.Status == ThreadStatus.Planning
            ? "\n\n🔍 " + InlineMarkdown(Truncate(plannerActivity.ReplaceLineEndings(" "), 120))
            : string.Empty;

        var tasks = new StringBuilder();
        var shown = 0;
        foreach (var task in detail.Tasks)
        {
            var line = $"{TaskIcon(task.Status)} <b>{Escape(task.TaskKey)}</b> {Escape(Truncate(task.Title, 60))} — <code>{Escape(ShortPath(task.FilePath, 40))}</code>\n";
            if (shown >= MaxListedTasks || header.Length + tasks.Length + line.Length + footer.Length > MaxStatusLength) break;
            tasks.Append(line);
            shown++;
        }
        if (shown < detail.Tasks.Count) tasks.Append($"… dan {detail.Tasks.Count - shown} tugas lain\n");

        return (header + (tasks.Length > 0 ? "\n\n" + tasks.ToString().TrimEnd() : string.Empty) + footer).Trim();
    }

    // First line of the answer message once the thread has ended.
    public static string ResultHeader(ThreadDetailDto detail, string projectName)
    {
        var s = detail.Summary;
        var (icon, label) = StatusLabel(s);
        var builder = new StringBuilder()
            .Append(icon).Append(" <b>").Append(label).Append("</b> · ").Append(Escape(projectName))
            .Append(" · thread <code>").Append(ShortId(s.Id)).Append("</code>\n");
        if (s.TaskCount > 0) builder.Append($"{s.TasksSucceeded}/{s.TaskCount} tugas berhasil · ");
        builder.Append(Duration(s.TotalMs ?? 0));
        if (!string.IsNullOrWhiteSpace(detail.Error)) builder.Append('\n').Append(Escape(Truncate(detail.Error, 500)));
        return builder.ToString();
    }

    public static string Status(string? activeProject, ThreadSummaryDto? running, IReadOnlyList<ThreadSummaryDto> recent, DateTime now)
    {
        var builder = new StringBuilder(ActiveProjectLine(activeProject));
        if (running is not null) builder.Append("\n▶️ Berjalan dari chat ini: ").Append(ThreadLine(running, now));
        builder.Append("\n\n🕘 <b>Thread terakhir</b>\n");
        if (recent.Count == 0) builder.Append("Belum ada thread.");
        foreach (var thread in recent) builder.Append(ThreadLine(thread, now)).Append('\n');
        return builder.ToString().TrimEnd();
    }

    public static string ThreadLine(ThreadSummaryDto t, DateTime now)
    {
        var duration = t.TotalMs ?? (IsActive(t.Status) ? (long)(now - t.CreatedAt).TotalMilliseconds : 0);
        return $"{StatusLabel(t).Icon} <code>{ShortId(t.Id)}</code> {Escape(t.ProjectName ?? "–")} · {SourceLabel(t.Source)} · {Duration(duration)}\n" +
               $"      <i>{Escape(Truncate(t.Prompt.ReplaceLineEndings(" "), 90))}</i>";
    }

    public static string Duration(long milliseconds)
    {
        var seconds = Math.Max(0, milliseconds) / 1000;
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m {seconds % 60:00}s";
        return $"{seconds / 3600}j {seconds % 3600 / 60:00}m";
    }

    // Keeps the end of long paths, cut at a folder boundary.
    public static string ShortPath(string path, int max = 48)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Length <= max) return normalized;
        var tail = normalized[^(max - 1)..];
        var slash = tail.IndexOf('/');
        return "…" + (slash > 0 ? tail[slash..] : tail);
    }

    public static string ShortId(Guid id) => id.ToString()[..8];

    private static string ActiveProjectLine(string? activeProject) => activeProject is null
        ? "📁 Belum ada project aktif — pilih dengan /projects."
        : $"📁 Project aktif: <b>{Escape(activeProject)}</b>";

    private static (string Icon, string Label) StatusLabel(ThreadSummaryDto s) => s.Status switch
    {
        ThreadStatus.Pending => ("⏳", "Menunggu"),
        ThreadStatus.Planning => ("🧠", "Planning"),
        ThreadStatus.Executing => ("⚙️", "Eksekusi"),
        ThreadStatus.Verifying => ("🔍", "Verifikasi"),
        ThreadStatus.Testing => ("🧪", "Testing"),
        ThreadStatus.Wrapping => ("📦", "Wrapping"),
        ThreadStatus.AwaitingInput => ("❓", "Menunggu jawaban"),
        ThreadStatus.Completed when s.TasksFailed > 0 => ("⚠️", "Selesai sebagian"),
        ThreadStatus.Completed => ("✅", "Selesai"),
        ThreadStatus.Failed => ("❌", "Gagal"),
        ThreadStatus.Cancelled => ("⛔", "Dibatalkan"),
        ThreadStatus.RolledBack => ("↩️", "Perubahan dibatalkan"),
        _ => ("•", s.Status.ToString())
    };

    private static string TaskIcon(ThreadTaskStatus status) => status switch
    {
        ThreadTaskStatus.Pending => "⏳",
        ThreadTaskStatus.Running => "🔄",
        ThreadTaskStatus.Succeeded => "✅",
        ThreadTaskStatus.Failed => "❌",
        ThreadTaskStatus.Skipped => "⏭️",
        ThreadTaskStatus.Cancelled => "⛔",
        _ => "•"
    };

    private static string SourceLabel(ThreadSource source) => source switch
    {
        ThreadSource.ClaudeCode => "CLI",
        ThreadSource.Telegram => "Telegram",
        _ => "Dashboard"
    };

    private static string Lines(params string[] lines) => string.Join("\n", lines);
}
