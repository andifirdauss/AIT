using System.Diagnostics;
using System.Runtime.Versioning;
using AIThread.Common.Chat;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

// The interactive aithread chat: read a prompt, start a thread on the project, follow it to the end (answering its
// permission requests), and back to the prompt. Every prompt is its own thread, and all of them belong to one session
// (ChatSessionKey): the planner gets the earlier turns, a planner question is answered by the next prompt, and the
// session can be resumed later (-c, -r, /resume). /new starts a new session.
[SupportedOSPlatform("windows")]
public sealed class ChatSession
{
    private static readonly TimeSpan ApprovalPoll = TimeSpan.FromSeconds(2);
    private const int RecapTurns = 5;

    private readonly ChatApiClient _api;
    private readonly ChatRenderer _renderer;
    private readonly ThreadFollower _follower;
    private readonly ImageAttachments _attachments = new();
    private readonly PromptEditor _editor;
    private ProjectDto _project;
    private Guid? _lastThread;
    private string _sessionId;
    private ChatSessionDto? _resumed;

    // resumed: the session to go on with (-c/-r); null starts a new one.
    public ChatSession(ChatApiClient api, ChatRenderer renderer, ProjectDto project, ChatSessionDto? resumed = null)
    {
        _api = api;
        _renderer = renderer;
        _project = project;
        _follower = new ThreadFollower(api, renderer);
        _editor = new PromptEditor(_attachments);
        _sessionId = resumed?.SessionId ?? ChatSessionKey.New();
        _resumed = resumed;
    }

    // What the developer answered to a permission request: a decision, Leave (answer later on the thread page), or
    // Elsewhere (the thread page answered first; Next is the thread that goes on, when known).
    private sealed record ApprovalAnswer(ApprovalDecision? Decision, bool Leave, bool Elsewhere, Guid? Next = null);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        _renderer.Banner(_project, _api.BaseUrl, ChatSessionKey.Short(_sessionId));
        if (_resumed is { } resumed)
        {
            try
            {
                await RecapAsync(resumed, cancellationToken);
            }
            catch (ChatApiException ex)
            {
                _renderer.Error(ex.Message);
            }
        }

        while (true)
        {
            var input = _editor.Read();
            if (input.Exit) return 0;

            var text = input.Text!.Trim();
            if (text.StartsWith('/') && await CommandAsync(text, cancellationToken) is { } keepGoing)
            {
                if (!keepGoing) return 0;
                continue;
            }

            // Pastes are expanded after the image markers were numbered again, so a pasted text stays as it was.
            var (numbered, images) = _attachments.Build(text);
            var prompt = _editor.ExpandPastes(numbered);
            _attachments.Clear();
            try
            {
                var threadId = await _api.StartThreadAsync(_project.Id, prompt, images, _sessionId, cancellationToken);
                // The prompt answered a planner question of the last thread: that thread goes on, from AwaitingInput.
                await RunThreadAsync(threadId, restarted: threadId == _lastThread, cancellationToken);
            }
            catch (ChatApiException ex)
            {
                _renderer.Error(ex.Message);
            }
        }
    }

    // null: not a chat command (the text is sent as a prompt); false: leave the chat.
    private async Task<bool?> CommandAsync(string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var argument = parts.Length > 1 ? parts[1] : null;
        try
        {
            switch (parts[0].ToLowerInvariant())
            {
                case "/help":
                    _renderer.Text(Help);
                    return true;
                case "/exit" or "/quit":
                    return false;
                case "/project":
                    await SwitchProjectAsync(argument, cancellationToken);
                    return true;
                case "/cancel":
                    if (_lastThread is not { } running) _renderer.Info(Lang.T("Belum ada thread di chat ini.", "No thread in this chat yet."));
                    else if (await _api.CancelAsync(running, cancellationToken)) _renderer.Success(Lang.T("Pembatalan dikirim.", "Cancellation sent."));
                    return true;
                case "/retry":
                    if (_lastThread is not { } failed) _renderer.Info(Lang.T("Belum ada thread di chat ini.", "No thread in this chat yet."));
                    else await RunThreadAsync(await _api.RetryAsync(failed, cancellationToken), restarted: true, cancellationToken);
                    return true;
                case "/open":
                    Cli.OpenBrowser(_lastThread is { } last ? _api.ThreadUrl(last) : _api.BaseUrl);
                    return true;
                case "/verbose":
                    _renderer.Verbose = !_renderer.Verbose;
                    _renderer.Info(_renderer.Verbose ? Lang.T("Output tugas ditampilkan.", "Task output shown.") : Lang.T("Output tugas disembunyikan.", "Task output hidden."));
                    return true;
                case "/clear":
                    _attachments.Clear();
                    _renderer.Info(Lang.T("Lampiran dihapus.", "Attachments removed."));
                    return true;
                case "/resume":
                    await ResumeAsync(argument, cancellationToken);
                    return true;
                case "/new":
                    StartNewSession();
                    return true;
                default:
                    return null;
            }
        }
        catch (ChatApiException ex)
        {
            _renderer.Error(ex.Message);
            return true;
        }
    }

    private async Task SwitchProjectAsync(string? wanted, CancellationToken cancellationToken)
    {
        var projects = await _api.GetProjectsAsync(cancellationToken);
        ProjectDto? project;
        if (string.IsNullOrWhiteSpace(wanted)) project = ChatCommand.PickProject(projects);
        else
        {
            var resolution = ProjectResolver.Resolve(projects, wanted, Environment.CurrentDirectory);
            if (resolution.Error is { } error)
            {
                _renderer.Error(error);
                return;
            }
            project = resolution.Project;
        }

        if (project is null) return;
        _project = project;
        _renderer.Success(Lang.T($"Project: {project.Name} ({project.RootPath})", $"Project: {project.Name} ({project.RootPath})"));
        // Sessions belong to a project.
        StartNewSession();
    }

    // /resume [id]: another session of the project goes on (picked from the list without an id).
    private async Task ResumeAsync(string? wanted, CancellationToken cancellationToken)
    {
        var sessions = (await _api.GetSessionsAsync(_project.Id, 50, cancellationToken)).Where(s => s.SessionId != _sessionId).ToList();
        ChatSessionDto? session;
        if (string.IsNullOrWhiteSpace(wanted))
        {
            if (sessions.Count == 0)
            {
                _renderer.Info(Lang.T("Belum ada sesi lain di project ini.", "No other session in this project yet."));
                return;
            }
            session = ChatCommand.PickSession(sessions);
            if (session is null)
            {
                StartNewSession();
                return;
            }
        }
        else
        {
            var resolution = ChatSessions.Find(sessions, wanted);
            if (resolution.Error is { } error)
            {
                _renderer.Error(error);
                return;
            }
            session = resolution.Session!;
        }

        _sessionId = session.SessionId;
        _attachments.Clear();
        await RecapAsync(session, cancellationToken);
    }

    private void StartNewSession()
    {
        _sessionId = ChatSessionKey.New();
        _lastThread = null;
        _attachments.Clear();
        _renderer.Success(Lang.T($"Sesi baru {ChatSessionKey.Short(_sessionId)}.", $"New session {ChatSessionKey.Short(_sessionId)}."));
    }

    // The last turns of a resumed session, so the developer sees where it stopped; its last thread becomes the one
    // /retry, /open and /cancel act on.
    private async Task RecapAsync(ChatSessionDto session, CancellationToken cancellationToken)
    {
        var threads = await _api.GetSessionThreadsAsync(_project.Id, session.SessionId, RecapTurns, cancellationToken);
        _lastThread = threads.FirstOrDefault()?.Id;
        _renderer.Recap(session, threads.Reverse().ToList(), DateTime.UtcNow);
    }

    // Follows the thread, and the thread that goes on after a permission answer, until one ends without a request.
    private async Task RunThreadAsync(Guid threadId, bool restarted, CancellationToken cancellationToken)
    {
        Guid? current = threadId;
        while (current is { } id)
        {
            _lastThread = id;
            var url = _api.ThreadUrl(id);
            var followed = await _follower.FollowAsync(id, interactive: true, restarted, cancellationToken);
            restarted = false;
            if (followed.Detached)
            {
                _renderer.Info(Lang.T($"Thread lanjut di latar belakang: {url} (/cancel membatalkan, /open membuka halamannya).", $"The thread goes on in the background: {url} (/cancel cancels it, /open opens its page)."));
                return;
            }

            var detail = followed.Detail!;
            var approval = await _follower.PendingApprovalAsync(detail, interactive: true, cancellationToken);
            if (approval is null)
            {
                _renderer.Summary(detail, url);
                return;
            }

            // A paused planner's answer is the request itself; a finished thread shows its result first.
            if (approval.AfterThread) _renderer.Summary(detail, url);
            _renderer.Approval(approval, url);

            var answer = await AskAsync(id, approval, cancellationToken);
            Guid? next;
            if (answer.Leave)
            {
                _renderer.Info(Lang.T($"Thread menunggu izin; jawab di {url}", $"The thread waits for permission; answer at {url}"));
                return;
            }
            if (answer.Elsewhere)
            {
                _renderer.Info(Lang.T("Izin sudah dijawab di tempat lain.", "The permission was answered elsewhere."));
                next = answer.Next;
            }
            else
            {
                next = await _api.ApproveAsync(id, answer.Decision!.Value, cancellationToken);
                _renderer.Info(answer.Decision == ApprovalDecision.Deny ? Lang.T("Izin ditolak.", "Permission denied.") : Lang.T("Izin diberikan.", "Permission given."));
            }

            if (next is null) return;
            if (approval.AfterThread)
            {
                // A commit/push ran on the finished thread and its result was added to the answer.
                var after = await _api.GetThreadAsync(id, cancellationToken);
                if (ThreadFollower.Added(detail.FinalResponse, after.FinalResponse) is { Length: > 0 } added) _renderer.Text(added);
                return;
            }

            // The paused planner goes on in the same thread (a new one only when it could not be resumed): its
            // AwaitingInput status must not end the next follow before the thread moved.
            current = next;
            restarted = next == id;
        }
    }

    // The choices as a list below the card: ↑/↓ and Enter, or a choice's key (y/a/p/s/n) or number (1-5 as in the chat
    // card) at once. The last entry, Esc or Ctrl+C leave the request waiting. The thread page may answer first: the
    // request disappears and the thread that went on is looked up.
    private async Task<ApprovalAnswer> AskAsync(Guid threadId, PendingApprovalDto approval, CancellationToken cancellationToken)
    {
        var options = ChatRenderer.ApprovalOptions(approval);
        var later = Lang.T("Nanti — biarkan menunggu, jawab di halaman thread", "Later — leave it waiting, answer on the thread page");
        var menu = new ChoiceMenu(
            [.. options.Select(o => $"{o.Key}  {ChatRenderer.Inline(o.Text, markup: false)}"), $"⎋  {later}"],
            Lang.T("↑/↓ pilih · Enter jawab · huruf/angka langsung · Esc nanti", "↑/↓ choose · Enter answer · letter/number at once · Esc later"));

        var treatControlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        ApprovalAnswer? answer = null;
        try
        {
            menu.Draw();
            var poll = Stopwatch.StartNew();
            while (true)
            {
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.C && (key.Modifiers & ConsoleModifiers.Control) != 0)
                        return answer = new ApprovalAnswer(null, Leave: true, Elsewhere: false);
                    if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                    {
                        menu.Move(key.Key == ConsoleKey.UpArrow ? -1 : 1);
                        continue;
                    }

                    var chosen = key.Key == ConsoleKey.Enter
                        ? options.ElementAtOrDefault(menu.Index)
                        : options.FirstOrDefault(o => char.ToLowerInvariant(key.KeyChar) == o.Key || key.KeyChar == o.Number);
                    if (chosen is not null) return answer = new ApprovalAnswer(chosen.Decision, Leave: false, Elsewhere: false);
                    if (key.Key == ConsoleKey.Enter) return answer = new ApprovalAnswer(null, Leave: true, Elsewhere: false);
                }

                if (poll.Elapsed >= ApprovalPoll)
                {
                    poll.Restart();
                    if (await _api.GetApprovalAsync(threadId, cancellationToken) is null)
                        // Answered on the thread page: a commit/push and a resumed planner both go on in this thread.
                        return answer = new ApprovalAnswer(null, Leave: false, Elsewhere: true, threadId);
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            Console.TreatControlCAsInput = treatControlC;
            menu.Close(answer?.Decision is { } decision ? ChatRenderer.Inline(options.First(o => o.Decision == decision).Text, markup: false) : null);
        }
    }

    private static string Help => Lang.T("Perintah:", "Commands:") + "\n" + SlashCommands.List() + "\n\n" + Lang.T(
        """
        Tombol:
          / buka daftar perintah (↑/↓ pilih · Tab lengkapi · Enter jalankan · Esc tutup)
          Enter kirim · Shift+Enter, Alt+Enter, Ctrl+J atau \ di akhir baris = baris baru · ↑/↓ riwayat
          Alt+V lampirkan gambar clipboard · Ctrl+V gambar atau teks clipboard (bila terminal meneruskannya, lihat docs) · tarik file gambar ke terminal
          @path/gambar.png lalu spasi/Enter lampirkan file (png, jpg, gif, webp; maks 6 gambar, 6 MB per gambar)
          Paste lebih dari 10 baris atau 1000 karakter menjadi [Paste #N, …], dikirim sebagai teks utuh
          Saat thread berjalan: Ctrl+C dua kali batalkan · Esc lepas (thread lanjut) · v output tugas
          Saat diminta izin: ↑/↓ + Enter, atau langsung y sekali · a selalu · p selalu untuk pola · s selalu semua perintah · n tolak · Esc nanti
        """,
        """
        Keys:
          / open the command list (↑/↓ choose · Tab complete · Enter run · Esc close)
          Enter send · Shift+Enter, Alt+Enter, Ctrl+J or a trailing \ = new line · ↑/↓ history
          Alt+V attach the clipboard image · Ctrl+V the clipboard image or text (when the terminal passes it on, see the docs) · drag image files onto the terminal
          @path/image.png then space/Enter attach a file (png, jpg, gif, webp; at most 6 images, 6 MB each)
          A paste over 10 lines or 1000 characters becomes [Paste #N, …], sent as the full text
          While a thread runs: Ctrl+C twice cancel · Esc detach (the thread goes on) · v task output
          When asked for permission: ↑/↓ + Enter, or at once y once · a always · p always for the pattern · s always every command · n deny · Esc later
        """);
}
