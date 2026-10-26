using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using static AIThread.Service.Telegram.TelegramFormatter;

namespace AIThread.Service.Telegram;

public sealed record TelegramProject(Guid Id, string Name, string RootPath);

// Everything the bot writes (in the language of AIT, Telegram HTML), plus the command and button formats it reads back.
// Lines are joined with "\n" explicitly: Telegram shows "\r" literally.
public static class TelegramMessages
{
    public static IReadOnlyList<TelegramCommand> Commands =>
    [
        new("projects", Lang.T("Pilih project untuk prompt", "Choose the project for prompts")),
        new("status", Lang.T("Thread berjalan & terakhir", "Running & latest threads")),
        new("cancel", Lang.T("Batalkan thread yang berjalan", "Cancel the running thread")),
        new("diff", Lang.T("Perubahan file thread terakhir", "File changes of the last thread")),
        new("new", Lang.T("Mulai percakapan baru", "Start a new conversation")),
        new("help", Lang.T("Bantuan", "Help"))
    ];

    // Inline button payloads: prefix + Guid "N" (34 bytes, within Telegram's 64-byte limit).
    public const string ProjectButton = "p:";
    public const string CancelButton = "c:";

    public static string TextOnly => Lang.T("Hanya pesan teks yang didukung. Kirim /help untuk bantuan.", "Only text messages are supported. Send /help for help.");
    public static string UnknownCommand => Lang.T("Perintah tidak dikenal. Kirim /help untuk daftar perintah.", "Unknown command. Send /help for the list of commands.");
    public static string NoProjects => Lang.T("Belum ada project terdaftar. Buka sesi di folder project (berisi <code>.git</code> atau file build) atau daftarkan foldernya di menu <b>Project</b> AIT terlebih dahulu.", "No project registered yet. Open a session in a project folder (with <code>.git</code> or a build file) or register its folder in the AIT <b>Project</b> menu first.");
    public static string ConversationReset => Lang.T("🆕 Percakapan baru. Prompt berikutnya dikirim tanpa riwayat sebelumnya.", "🆕 New conversation. The next prompt is sent without the earlier history.");
    public static string NothingToCancel => Lang.T("Tidak ada thread berjalan dari chat ini. Untuk thread lain: <code>/cancel &lt;id&gt;</code> (lihat /status).", "No thread running from this chat. For another thread: <code>/cancel &lt;id&gt;</code> (see /status).");
    public static string NoThreadYet => Lang.T("Belum ada thread dari chat ini.", "No thread from this chat yet.");
    public static string Revoked => Lang.T("🔒 Akses chat ini ke AIT dicabut.", "🔒 This chat's access to AIT was revoked.");
    public static string DiffTruncated => Lang.T("… diff terlalu panjang untuk Telegram; lihat detail thread di Dasbor AIT.", "… the diff is too long for Telegram; see the thread detail in the AIT dashboard.");

    private const int MaxListedTasks = 25;
    private const int MaxListedProjects = 30;
    private const int MaxStatusLength = 3800;


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
        Lang.T("🧵 <b>AIT</b> — prompt jarak jauh", "🧵 <b>AIT</b> — remote prompts"),
        "",
        Lang.T("Kirim pesan biasa untuk menjalankan prompt di project aktif. Planner menyusun rencana, executor mengerjakan paralel, lalu ringkasannya dikirim ke sini.", "Send a regular message to run a prompt in the active project. The planner makes a plan, executors work in parallel, then the summary is sent here."),
        "",
        ActiveProjectLine(activeProject),
        "",
        Lang.T("/projects — pilih project", "/projects — choose a project"),
        Lang.T("/status — thread berjalan &amp; terakhir", "/status — running &amp; latest threads"),
        Lang.T("/cancel — batalkan thread yang berjalan", "/cancel — cancel the running thread"),
        Lang.T("/diff — perubahan file thread terakhir", "/diff — file changes of the last thread"),
        Lang.T("/new — mulai percakapan baru", "/new — start a new conversation"),
        Lang.T("/help — bantuan ini", "/help — this help"),
        "",
        Lang.T("<i>Prompt lanjutan di project yang sama membawa percakapan sebelumnya sebagai konteks, sampai /new atau ganti project.</i>", "<i>Follow-up prompts in the same project carry the earlier conversation as context, until /new or a project change.</i>"));

    public static string NotAllowed(string displayName, long chatId) => Lines(
        Lang.T("🔒 Chat ini belum diizinkan memakai AIT.", "🔒 This chat is not allowed to use AIT yet."),
        "",
        Lang.T($"Di komputer yang menjalankan AIT, buka <b>Pengaturan → Telegram</b> lalu klik <b>Izinkan</b> pada chat <b>{Escape(displayName)}</b> (ID <code>{chatId}</code>).", $"On the computer running AIT, open <b>Setting → Telegram</b> and click <b>Allow</b> on the chat <b>{Escape(displayName)}</b> (ID <code>{chatId}</code>)."));

    public static string Allowed(string? activeProject) => Lines(Lang.T("✅ Chat ini sekarang diizinkan memakai AIT.", "✅ This chat is now allowed to use AIT."), "", Help(activeProject));

    public static string Stale(DateTime sentAtUtc) =>
        Lang.T($"⏸️ Pesan ini terkirim saat AIT tidak berjalan ({sentAtUtc.ToLocalTime().ToString("d MMM HH:mm", Lang.Culture)}) sehingga tidak dijalankan. Kirim ulang bila masih diperlukan.", $"⏸️ This message arrived while AIT was not running ({sentAtUtc.ToLocalTime().ToString("d MMM HH:mm", Lang.Culture)}), so it was not run. Send it again if it is still needed.");

    public static string Starting(string projectName) => $"🧵 <b>{Escape(projectName)}</b> · menyiapkan thread…";

    public static string StartFailed(string error) => Lang.T($"❌ Thread tidak dapat dimulai: {Escape(error)}", $"❌ The thread could not start: {Escape(error)}");

    public static string StillRunning(Guid threadId) =>
        Lang.T($"⏳ Thread <code>{ShortId(threadId)}</code> masih berjalan. Tunggu sampai selesai atau kirim /cancel.", $"⏳ Thread <code>{ShortId(threadId)}</code> is still running. Wait until it finishes or send /cancel.");

    public static string Failure(string error) => Lang.T($"⚠️ Terjadi kesalahan di AIT: {Escape(error)}", $"⚠️ An error occurred in AIT: {Escape(error)}");

    public static string Interrupted(Guid threadId) =>
        Lang.T($"⚠️ AIT berhenti saat thread <code>{ShortId(threadId)}</code> berjalan, jadi thread ini tidak selesai. ", $"⚠️ AIT stopped while thread <code>{ShortId(threadId)}</code> was running, so this thread did not finish. ") +
        Lang.T($"Tugas yang sempat selesai tetap tertulis — cek <code>/diff {ShortId(threadId)}</code> setelah AIT hidup lagi, lalu kirim ulang prompt bila perlu.", $"Tasks that finished are still written — check <code>/diff {ShortId(threadId)}</code> once AIT is up again, then send the prompt again if needed.");

    public static string ProjectList(IReadOnlyList<TelegramProject> projects, Guid? activeId, string? intro)
    {
        var builder = new StringBuilder(intro ?? Lang.T("📁 <b>Pilih project</b> untuk prompt berikutnya:", "📁 <b>Choose a project</b> for the next prompt:")).Append("\n\n");
        for (var i = 0; i < projects.Count && i < MaxListedProjects; i++)
        {
            var p = projects[i];
            builder.Append(i + 1).Append(". ").Append(p.Id == activeId ? "✅ " : string.Empty)
                .Append("<b>").Append(Escape(p.Name)).Append("</b> — <code>").Append(Escape(ShortPath(p.RootPath))).Append("</code>\n");
        }
        if (projects.Count > MaxListedProjects)
            builder.Append(Lang.T($"… dan {projects.Count - MaxListedProjects} project lain — ketik <code>/project &lt;nama&gt;</code>\n", $"… and {projects.Count - MaxListedProjects} more projects — type <code>/project &lt;name&gt;</code>\n"));
        return builder.ToString().TrimEnd();
    }

    public static TgInlineKeyboard ProjectKeyboard(IReadOnlyList<TelegramProject> projects, Guid? activeId) =>
        new(projects.Take(MaxListedProjects)
            .Select(p => (IReadOnlyList<TgInlineButton>)[new TgInlineButton((p.Id == activeId ? "✅ " : string.Empty) + Truncate(p.Name, 40), ButtonData(ProjectButton, p.Id))])
            .ToList());

    public static string ProjectNotFound(string argument) => Lang.T($"Project \"{Escape(argument)}\" tidak ditemukan. Pilih dari daftar:", $"Project \"{Escape(argument)}\" not found. Choose from the list:");

    public static string ProjectSelected(string name, bool runningPendingPrompt) => runningPendingPrompt
        ? Lang.T($"📁 Project aktif: <b>{Escape(name)}</b>. Menjalankan prompt Anda…", $"📁 Active project: <b>{Escape(name)}</b>. Running your prompt…")
        : Lang.T($"📁 Project aktif: <b>{Escape(name)}</b>. Kirim prompt Anda.", $"📁 Active project: <b>{Escape(name)}</b>. Send your prompt.");

    public static string PickProjectForPrompt => Lang.T("📁 Pilih project untuk prompt ini — prompt langsung dijalankan setelah Anda memilih:", "📁 Choose a project for this prompt — it runs right after you choose:");

    public static TgInlineKeyboard CancelKeyboard(Guid threadId) =>
        new([[new TgInlineButton(Lang.T("⛔ Batalkan", "⛔ Cancel"), ButtonData(CancelButton, threadId))]]);

    public static string CancelSent(Guid threadId) => Lang.T($"⛔ Pembatalan dikirim ke thread <code>{ShortId(threadId)}</code>.", $"⛔ Cancellation sent to thread <code>{ShortId(threadId)}</code>.");

    public static string NotRunning(Guid threadId) => Lang.T($"Thread <code>{ShortId(threadId)}</code> sudah tidak berjalan.", $"Thread <code>{ShortId(threadId)}</code> is no longer running.");

    public static string NoRunningMatch(string argument) => Lang.T($"Tidak ada thread berjalan dengan id <code>{Escape(argument)}</code>.", $"No running thread with id <code>{Escape(argument)}</code>.");

    public static string ThreadNotFound(string argument) => Lang.T($"Thread <code>{Escape(argument)}</code> tidak ditemukan.", $"Thread <code>{Escape(argument)}</code> not found.");

    public static string NoChanges(Guid threadId) => Lang.T($"Thread <code>{ShortId(threadId)}</code> tidak mengubah file.", $"Thread <code>{ShortId(threadId)}</code> changed no files.");

    public static string DiffHeader(Guid threadId, int files) => Lang.T($"🧾 Perubahan thread <code>{ShortId(threadId)}</code> · {files} file", $"🧾 Changes of thread <code>{ShortId(threadId)}</code> · {files} file(s)");

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
        if (s.TaskCount > 0) header.Append(Lang.T($" · {s.TasksSucceeded}/{s.TaskCount} tugas", $" · {s.TasksSucceeded}/{s.TaskCount} tasks"));
        if (s.TasksFailed > 0) header.Append(Lang.T($" · {s.TasksFailed} gagal", $" · {s.TasksFailed} failed"));
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
        if (shown < detail.Tasks.Count) tasks.Append(Lang.T($"… dan {detail.Tasks.Count - shown} tugas lain\n", $"… and {detail.Tasks.Count - shown} more tasks\n"));

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
        if (s.TaskCount > 0) builder.Append(Lang.T($"{s.TasksSucceeded}/{s.TaskCount} tugas berhasil · ", $"{s.TasksSucceeded}/{s.TaskCount} tasks succeeded · "));
        builder.Append(Duration(s.TotalMs ?? 0));
        if (!string.IsNullOrWhiteSpace(detail.Error)) builder.Append('\n').Append(Escape(Truncate(detail.Error, 500)));
        return builder.ToString();
    }

    public static string Status(string? activeProject, ThreadSummaryDto? running, IReadOnlyList<ThreadSummaryDto> recent, DateTime now)
    {
        var builder = new StringBuilder(ActiveProjectLine(activeProject));
        if (running is not null) builder.Append(Lang.T("\n▶️ Berjalan dari chat ini: ", "\n▶️ Running from this chat: ")).Append(ThreadLine(running, now));
        builder.Append(Lang.T("\n\n🕘 <b>Thread terakhir</b>\n", "\n\n🕘 <b>Latest threads</b>\n"));
        if (recent.Count == 0) builder.Append(Lang.T("Belum ada thread.", "No thread yet."));
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
        ? Lang.T("📁 Belum ada project aktif — pilih dengan /projects.", "📁 No active project yet — choose one with /projects.")
        : Lang.T($"📁 Project aktif: <b>{Escape(activeProject)}</b>", $"📁 Active project: <b>{Escape(activeProject)}</b>");

    private static (string Icon, string Label) StatusLabel(ThreadSummaryDto s) => s.Status switch
    {
        ThreadStatus.Pending => ("⏳", Lang.T("Menunggu", "Pending")),
        ThreadStatus.Planning => ("🧠", "Planning"),
        ThreadStatus.Executing => ("⚙️", Lang.T("Eksekusi", "Executing")),
        ThreadStatus.Verifying => ("🔍", Lang.T("Verifikasi", "Verifying")),
        ThreadStatus.Testing => ("🧪", "Testing"),
        ThreadStatus.Wrapping => ("📦", "Wrapping"),
        ThreadStatus.AwaitingInput => ("❓", Lang.T("Menunggu jawaban", "Awaiting answer")),
        ThreadStatus.Completed when s.TasksFailed > 0 => ("⚠️", Lang.T("Selesai sebagian", "Partly completed")),
        ThreadStatus.Completed => ("✅", Lang.T("Selesai", "Completed")),
        ThreadStatus.Failed => ("❌", Lang.T("Gagal", "Failed")),
        ThreadStatus.Cancelled => ("⛔", Lang.T("Dibatalkan", "Cancelled")),
        ThreadStatus.RolledBack => ("↩️", Lang.T("Perubahan dibatalkan", "Changes rolled back")),
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
        _ => Lang.T("Dasbor", "Dashboard")
    };

    private static string Lines(params string[] lines) => string.Join("\n", lines);
}
