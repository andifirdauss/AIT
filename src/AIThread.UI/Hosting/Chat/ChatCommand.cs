using System.Runtime.Versioning;
using System.Text;
using AIThread.Common.Chat;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using Spectre.Console;

namespace AIThread.UI.Hosting.Chat;

// Project: --project (name or folder). Prompt: -p, the non-interactive mode. Yes: -y, permission requests are
// answered "once" under -p. Continue: -c, the project's most recent chat session goes on. Resume: -r, the session with
// that id; empty: pick one from the list.
public sealed record ChatOptions(string? Project, string? Prompt, bool Yes, bool Continue = false, string? Resume = null);

// aithread chat: a terminal chat with the AIT pipeline of a running instance (started in the tray when none runs; the
// chat never hosts the server itself). Exit codes of -p: 0 completed, 1 failed/cancelled or no server, 2 wrong
// arguments or no project, 3 waiting for permission without -y.
[SupportedOSPlatform("windows")]
public static class ChatCommand
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int BadArguments = 2;
    public const int AwaitingApproval = 3;

    public static async Task<int> RunAsync(int port, ChatOptions options)
    {
        var promptMode = options.Prompt is not null;
        if (options.Yes && !promptMode)
            return Refuse(Lang.T("-y hanya berlaku bersama -p.", "-y only works together with -p."));
        if (promptMode && string.IsNullOrWhiteSpace(options.Prompt))
            return Refuse(Lang.T("-p butuh teks prompt.", "-p needs the prompt text."));
        if (!promptMode && (Console.IsInputRedirected || Console.IsOutputRedirected))
            return Refuse(Lang.T("Chat interaktif butuh terminal; untuk skrip pakai -p \"<prompt>\".", "The interactive chat needs a terminal; for scripts use -p \"<prompt>\"."));
        if (options.Continue && options.Resume is not null)
            return Refuse(Lang.T("Pakai -c atau -r, tidak keduanya.", "Use -c or -r, not both."));
        if (promptMode && options.Resume == string.Empty)
            return Refuse(Lang.T("-r tanpa id membuka daftar pilihan; bersama -p tulis id-nya (-r <id>) atau pakai -c.", "-r without an id opens a list to pick from; with -p write the id (-r <id>) or use -c."));

        // Before Spectre reads the console profile.
        Console.OutputEncoding = Encoding.UTF8;
        if (!promptMode) Console.InputEncoding = Encoding.UTF8;
        var renderer = ChatRenderer.ForConsole(forcePlain: promptMode);

        if (!await EnsureServerAsync(port, renderer)) return Failed;

        using var api = new ChatApiClient(port);
        using var quit = new CancellationTokenSource();
        try
        {
            var projects = await api.GetProjectsAsync(quit.Token);
            var resolution = ProjectResolver.Resolve(projects, options.Project, Environment.CurrentDirectory);
            if (resolution.Error is { } error)
            {
                renderer.Error(error);
                return BadArguments;
            }

            var project = resolution.Project;
            if (project is null)
            {
                if (projects.Count == 0)
                {
                    renderer.Error(Lang.T("Belum ada project terdaftar. Daftarkan lewat Web UI → Project.", "No project is registered yet. Register one in the Web UI → Project."));
                    return BadArguments;
                }
                if (promptMode)
                {
                    renderer.Error(Lang.T("Folder ini bukan bagian dari project terdaftar; pakai --project <nama|path>.", "This folder is not inside a registered project; use --project <name|path>."));
                    return BadArguments;
                }
                project = PickProject(projects);
            }

            var session = await StartingSessionAsync(api, renderer, project, options, quit.Token);
            if (session.Error is { } sessionError)
            {
                renderer.Error(sessionError);
                return BadArguments;
            }

            return promptMode
                ? await RunPromptAsync(api, renderer, project, options, session.Session?.SessionId ?? ChatSessionKey.New(), quit.Token)
                : await new ChatSession(api, renderer, project, session.Session).RunAsync(quit.Token);
        }
        catch (ChatApiException ex)
        {
            renderer.Error(ex.Message);
            return Failed;
        }
    }

    public static ProjectDto PickProject(IReadOnlyList<ProjectDto> projects)
    {
        if (projects.Count == 1) return projects[0];
        return AnsiConsole.Prompt(new SelectionPrompt<ProjectDto>()
            .Title(Markup.Escape(Lang.T("Pilih project:", "Pick a project:")))
            .PageSize(12)
            .EnableSearch()
            .UseConverter(p => $"{Markup.Escape(p.Name)} [grey]{Markup.Escape(p.RootPath)}[/]")
            .AddChoices(projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)));
    }

    // The session to go on with: -c the most recent one (a new session when the project has none yet), -r <id> that one,
    // -r alone one picked from the list; none of them: a new session (Session null).
    private static async Task<SessionResolution> StartingSessionAsync(ChatApiClient api, ChatRenderer renderer, ProjectDto project, ChatOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.Continue && options.Resume is null) return new SessionResolution(null, null);

        var sessions = await api.GetSessionsAsync(project.Id, 50, cancellationToken);
        if (options.Continue)
        {
            if (sessions.Count == 0) renderer.Info(Lang.T("Belum ada sesi chat di project ini; memulai sesi baru.", "No chat session in this project yet; starting a new one."));
            return new SessionResolution(sessions.FirstOrDefault(), null);
        }

        return options.Resume!.Length > 0 ? ChatSessions.Find(sessions, options.Resume) : new SessionResolution(PickSession(sessions), null);
    }

    // The list of /resume and -r: the project's sessions, most recent first, and a new session; null = a new session.
    public static ChatSessionDto? PickSession(IReadOnlyList<ChatSessionDto> sessions)
    {
        var fresh = new ChatSessionDto(string.Empty, string.Empty, string.Empty, 0, default, default, default);
        var now = DateTime.UtcNow;
        var picked = AnsiConsole.Prompt(new SelectionPrompt<ChatSessionDto>()
            .Title(Markup.Escape(Lang.T("Lanjutkan sesi chat:", "Resume a chat session:")))
            .PageSize(12)
            .EnableSearch()
            .UseConverter(s => s.SessionId.Length == 0
                ? $"[green]{Markup.Escape(Lang.T("+ Sesi baru", "+ New session"))}[/]"
                : Markup.Escape(ChatSessions.Describe(s, now)))
            .AddChoices([.. sessions, fresh]));
        return picked.SessionId.Length == 0 ? null : picked;
    }

    // The running instance, or a new one in the tray (as 'AIThread.exe tray'), waited for up to 30 seconds.
    private static async Task<bool> EnsureServerAsync(int port, ChatRenderer renderer)
    {
        using var client = new ServerClient(port);
        if (await client.GetInfoAsync() is not null) return true;

        renderer.Info(Lang.T($"AIT belum berjalan; menjalankannya di latar belakang ({client.BaseUrl}) ...", $"AIT is not running; starting it in the background ({client.BaseUrl}) ..."));
        try
        {
            Cli.SpawnTray(port);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            renderer.Error(Lang.T($"AIT tidak bisa dijalankan: {ex.Message}", $"AIT could not be started: {ex.Message}"));
            return false;
        }

        if (await Cli.WaitUntilAsync(async () => await client.GetInfoAsync() is not null, TimeSpan.FromSeconds(30))) return true;
        renderer.Error(Lang.T($"AIT belum menjawab di {client.BaseUrl} setelah 30 detik. Log: {AppPaths.LogDir}", $"AIT did not answer at {client.BaseUrl} after 30 seconds. Log: {AppPaths.LogDir}"));
        return false;
    }

    // -p: one prompt, plain progress on stdout, errors on stderr, the answer at the end.
    private static async Task<int> RunPromptAsync(ChatApiClient api, ChatRenderer renderer, ProjectDto project, ChatOptions options, string sessionId,
        CancellationToken cancellationToken)
    {
        var attachments = new ImageAttachments();
        if (AttachPaths(options.Prompt!, attachments, out var problem) is not { } text)
        {
            renderer.Error(problem!);
            return BadArguments;
        }

        var follower = new ThreadFollower(api, renderer);
        // First Ctrl+C cancels the thread and waits for it to stop; a second one ends the process.
        Console.CancelKeyPress += (_, e) =>
        {
            if (follower.CancelRequested) return;
            e.Cancel = true;
            follower.CancelRequested = true;
        };

        var (prompt, images) = attachments.Build(text);
        // A planner question waiting in a resumed session is answered by this prompt: the same thread goes on.
        var waiting = options.Continue || options.Resume is not null
            ? (await api.GetSessionThreadsAsync(project.Id, sessionId, 1, cancellationToken)).FirstOrDefault()?.Id
            : null;
        var threadId = await api.StartThreadAsync(project.Id, prompt, images, sessionId, cancellationToken);
        var shortId = ChatSessionKey.Short(sessionId);
        renderer.Info(Lang.T($"Thread {threadId} · {project.Name} · sesi {shortId} (lanjutkan: -r {shortId})",
            $"Thread {threadId} · {project.Name} · session {shortId} (resume: -r {shortId})"));

        var restarted = threadId == waiting;
        while (true)
        {
            var url = api.ThreadUrl(threadId);
            var detail = (await follower.FollowAsync(threadId, interactive: false, restarted, cancellationToken)).Detail!;
            var approval = follower.CancelRequested ? null : await follower.PendingApprovalAsync(detail, interactive: false, cancellationToken);
            if (approval is null)
            {
                renderer.Summary(detail, url);
                return detail.Summary.Status == ThreadStatus.Completed ? Ok : Failed;
            }

            if (approval.AfterThread) renderer.Summary(detail, url);
            renderer.Approval(approval, url);
            if (!options.Yes)
            {
                renderer.Error(Lang.T($"Thread menunggu izin. Jawab di {url}, atau jalankan dengan -y untuk mengizinkan sekali.", $"The thread waits for permission. Answer at {url}, or run with -y to allow it once."));
                return AwaitingApproval;
            }

            renderer.Info(Lang.T("-y: izin diberikan sekali.", "-y: permission given once."));
            var next = await api.ApproveAsync(threadId, ApprovalDecision.Once, cancellationToken);
            if (approval.AfterThread)
            {
                var after = await api.GetThreadAsync(threadId, cancellationToken);
                if (ThreadFollower.Added(detail.FinalResponse, after.FinalResponse) is { Length: > 0 } added) renderer.Text(added);
                return after.Summary.Status == ThreadStatus.Completed ? Ok : Failed;
            }

            // The paused planner goes on in the same thread; its AwaitingInput status must not end the next follow.
            restarted = next == threadId;
            threadId = next;
        }
    }

    // "@path" image tokens of the prompt become [Image #N] attachments, numbered in order; null with the problem when
    // an image is refused.
    public static string? AttachPaths(string prompt, ImageAttachments attachments, out string? problem)
    {
        var text = new StringBuilder();
        var position = 0;
        foreach (var token in ImageAttachments.FindPathTokens(prompt))
        {
            text.Append(prompt, position, token.Start - position);
            var image = ImageAttachments.Load(token.Path, Environment.CurrentDirectory, ImageAttachments.NextNumber(text.ToString()), out problem);
            if (image is null || attachments.Add(image, text.ToString(), out problem) is not { } marker) return null;
            text.Append(marker);
            position = token.Start + token.Length;
        }

        text.Append(prompt, position, prompt.Length - position);
        problem = null;
        return text.ToString();
    }

    private static int Refuse(string message)
    {
        Console.Error.WriteLine(message);
        return BadArguments;
    }
}
