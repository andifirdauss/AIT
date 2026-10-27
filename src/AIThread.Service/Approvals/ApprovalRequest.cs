using System.Text;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Approvals;

// One action AIT wants to take: what kind, on what (see ApprovalKind) and a line saying what exactly happens.
// Pattern: for a command, the pattern "always allow the pattern" saves instead (ApprovalTargets.SuggestPattern).
public sealed record ApprovalItem(ApprovalKind Kind, string Target, string? Detail = null, string? Pattern = null);

// What AIT asks the developer before acting: the actions, why, and whether "allow always" may be offered (never for
// a push to a protected branch).
public sealed record ApprovalRequest(IReadOnlyList<ApprovalItem> Items, string Reason, bool AllowAlways = true)
{
    // "Always allow the pattern" is offered: a command item has a pattern and "always" may be offered at all.
    public bool AllowPattern => AllowAlways && Items.Any(i => i.Pattern is not null);

    // Shown to the developer (CLI, Telegram, the thread page) as the thread's reply.
    public string ToText()
    {
        var builder = new StringBuilder(Lang.T("🔐 **AIT minta izin**\n\n", "🔐 **AIT asks for permission**\n\n"));
        if (!string.IsNullOrWhiteSpace(Reason)) builder.Append(Reason.Trim()).Append("\n\n");
        foreach (var item in Items)
        {
            builder.Append("- **").Append(KindLabel(item.Kind)).Append("** `").Append(item.Target).Append('`');
            if (!string.IsNullOrWhiteSpace(item.Detail)) builder.Append(" — ").Append(item.Detail.Trim());
            builder.Append('\n');
        }

        builder.Append('\n').Append(Lang.T("1. **Izinkan sekali** — hanya untuk aksi ini\n", "1. **Allow once** — for this action only\n"));
        if (AllowAlways)
            builder.Append(Lang.T("2. **Izinkan selalu** — disimpan di mesin ini untuk project ini (cabut di Project → Detail → Izin AIT)\n",
                "2. **Always allow** — saved on this machine for this project (revoke in Project → Detail → AIT permissions)\n"));
        builder.Append(Lang.T("3. **Tolak**\n", "3. **Deny**\n"));
        if (AllowPattern)
        {
            var patterns = string.Join(", ", Items.Where(i => i.Pattern is not null).Select(i => $"`{i.Pattern}`").Distinct());
            builder.Append(Lang.T($"4. **Izinkan selalu untuk pola** {patterns} — perintah lain yang diawali kata yang sama juga diizinkan\n",
                $"4. **Always allow the pattern** {patterns} — other commands starting with the same words are allowed too\n"));
        }
        builder.Append(Lang.T("\nBalas dengan nomor pilihan.", "\nReply with the option number."));
        return builder.ToString();
    }

    // For the buttons on the thread page; afterThread: a commit/push of a finished thread rather than a paused planner.
    public PendingApprovalDto ToDto(bool afterThread) =>
        new(Items.Select(i => new ApprovalItemDto(i.Kind, i.Target, i.Detail, i.Pattern)).ToList(), Reason, AllowAlways, ToText(), afterThread, AllowPattern);

    public static string KindLabel(ApprovalKind kind) => kind switch
    {
        ApprovalKind.WriteOutside => Lang.T("Ubah file di luar project", "Change files outside the project"),
        ApprovalKind.Command => Lang.T("Jalankan perintah", "Run a command"),
        ApprovalKind.Host => Lang.T("Akses host luar", "Call an outside host"),
        ApprovalKind.Commit => Lang.T("Commit", "Commit"),
        ApprovalKind.Push => Lang.T("Push", "Push"),
        ApprovalKind.Branch => Lang.T("Buat branch", "Create a branch"),
        ApprovalKind.PullRequest => Lang.T("Buka pull request", "Open a pull request"),
        _ => kind.ToString()
    };
}
