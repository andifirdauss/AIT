using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Orchestration;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AIThread.UI.Hosting.Chat;

// One answer to a permission request: Key and Number pick it at once, Text is its inline Markdown label.
public sealed record ApprovalOption(char Key, char Number, ApprovalDecision Decision, string Text);

// How aithread chat shows a thread. Rich: Spectre.Console, the progress as a live block (status with spinner, the
// planner's last tools, the task table) that stays on screen when the thread ends. Plain (output redirected, or -p):
// one line per change without ANSI, errors to stderr.
public sealed partial class ChatRenderer(bool rich)
{
    private static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public bool Rich { get; } = rich;

    public bool Verbose { get; set; }

    public static ChatRenderer ForConsole(bool forcePlain) =>
        new(!forcePlain && !Console.IsOutputRedirected && AnsiConsole.Profile.Capabilities.Ansi);

    public void Info(string text)
    {
        if (Rich) AnsiConsole.MarkupLine($"[grey]{Markup.Escape(text)}[/]");
        else Console.Out.WriteLine(text);
    }

    public void Success(string text)
    {
        if (Rich) AnsiConsole.MarkupLine($"[green]{Markup.Escape(text)}[/]");
        else Console.Out.WriteLine(text);
    }

    public void Error(string text)
    {
        if (Rich) AnsiConsole.MarkupLine($"[red]{Markup.Escape(text)}[/]");
        else Console.Error.WriteLine(text);
    }

    public void Text(string text)
    {
        if (Rich) AnsiConsole.Write(new Text(text + "\n"));
        else Console.Out.WriteLine(text);
    }

    public void Banner(ProjectDto project, string baseUrl, string session)
    {
        var sessionLabel = Lang.T($"sesi {session}", $"session {session}");
        if (!Rich)
        {
            Console.Out.WriteLine($"AIT chat · {project.Name} ({project.RootPath}) · {baseUrl} · {sessionLabel}");
            return;
        }

        AnsiConsole.Write(new Rule($"[bold]AIT chat[/] [grey]·[/] {Markup.Escape(project.Name)}").LeftJustified().RuleStyle("grey"));
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(project.RootPath)} · {Markup.Escape(baseUrl)} · {Markup.Escape(sessionLabel)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Lang.T("Ketik prompt lalu Enter. /help untuk perintah, /resume sesi lain, Ctrl+C keluar.", "Type a prompt and press Enter. /help for commands, /resume another session, Ctrl+C to quit."))}[/]");
        AnsiConsole.WriteLine();
    }

    // Where a resumed session stopped: its last turns (oldest first), each prompt with its result.
    public void Recap(ChatSessionDto session, IReadOnlyList<ThreadSummaryDto> turns, DateTime now)
    {
        var header = Lang.T($"Melanjutkan sesi {ChatSessions.Describe(session, now)}", $"Resuming session {ChatSessions.Describe(session, now)}");
        var hidden = session.Turns - turns.Count;
        if (!Rich)
        {
            Console.Out.WriteLine(header);
            foreach (var turn in turns) Console.Out.WriteLine($"  › {OneLine(turn.Prompt, 160)} [{StatusLabel(turn.Status)}]");
            return;
        }

        AnsiConsole.Write(new Rule($"[grey]{Markup.Escape(header)}[/]").LeftJustified().RuleStyle("grey"));
        if (hidden > 0)
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Lang.T($"… {hidden} prompt sebelumnya", $"… {hidden} earlier prompt{(hidden == 1 ? "" : "s")}"))}[/]");
        foreach (var turn in turns)
        {
            AnsiConsole.MarkupLine($"[bold]›[/] {Markup.Escape(OneLine(turn.Prompt, 200))}");
            var result = turn.PlanSummary is { Length: > 0 } plan ? OneLine(plan, 200) : StatusLabel(turn.Status);
            AnsiConsole.MarkupLine($"  [{StatusColor(turn.Status)}]{Markup.Escape(Icon(turn.Status))}[/] [grey]{Markup.Escape(result)}[/]");
        }
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(Lang.T("Prompt berikutnya melanjutkan sesi ini; planner membaca giliran sebelumnya.", "The next prompt goes on with this session; the planner reads the earlier turns."))}[/]");
        AnsiConsole.WriteLine();
    }

    // The live block of a running thread (rich): a vertical stepper of the thread's stages (Planner, Execution,
    // Verification, Testing, Summary), each with its state and time. The running stage shows what happens in it (the
    // planner's tools, the tasks, the log of the checks); a finished stage folds into one line, so the block left on
    // screen when the thread ends stays short. The answer and the changed files follow in Summary.
    //
    // The rail (│) runs down the left column beside every row of a stage, also beside the screen lines of a wrapped
    // entry: the content column has a fixed width, so each entry is measured at that width and the rail and the
    // continuation under its branch are repeated for every line it takes.
    public IRenderable Progress(ChatThreadState state, bool interactive)
    {
        const int timeWidth = 7;
        var rows = new List<IRenderable>();
        var spinner = Spinner[(int)(state.Clock.ElapsedMilliseconds / 100 % Spinner.Length)];
        // Glyph column 1 + 1 padding, content padding 2, time column.
        var contentWidth = Math.Max(20, AnsiConsole.Profile.Width - 4 - timeWidth);
        var textWidth = contentWidth - 3;
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap().PadRight(1))
            .AddColumn(new GridColumn().Width(contentWidth).PadRight(2))
            .AddColumn(new GridColumn().NoWrap().Width(timeWidth).RightAligned());

        if (state.Status == ThreadStatus.Pending)
            grid.AddRow(new Markup($"[cyan]{spinner}[/]"), new Markup($"[cyan]{Markup.Escape(StatusLabel(ThreadStatus.Pending))}[/]"),
                new Markup($"[grey]{Seconds(state.Clock.ElapsedMilliseconds)}[/]"));

        foreach (var phase in state.Phases())
        {
            var (glyph, color) = phase.State switch
            {
                PhaseState.Running => (spinner, "cyan"),
                PhaseState.Done when PhaseFailed(phase.Phase, state) => ("✖", "red"),
                PhaseState.Done => ("✔", "green"),
                PhaseState.Stopped => (Icon(state.Status), StatusColor(state.Status)),
                _ => ("·", "grey")
            };
            var escaped = Markup.Escape(PhaseTitle(phase.Phase, state));
            var title = new Markup(phase.State switch { PhaseState.Waiting => $"[grey]{escaped}[/]", PhaseState.Running => $"[bold]{escaped}[/]", _ => escaped });
            var body = PhaseBody(phase, state, textWidth);
            // A wrapped title keeps the rail below its glyph when entries follow.
            grid.AddRow(
                new Markup($"[{color}]{glyph}[/]" + Repeat(body.Count > 0 ? "\n[grey]│[/]" : "\n ", LinesOf(title, contentWidth) - 1)),
                title,
                new Markup($"[grey]{(phase.Ms is { } ms ? Seconds(ms) : string.Empty)}[/]"));

            // One entry per line under the stage; the last closes it with └─ once the stage stopped growing.
            for (var i = 0; i < body.Count; i++)
            {
                var last = i == body.Count - 1 && phase.State != PhaseState.Running;
                var lines = LinesOf(body[i], textWidth);
                var line = new Grid().AddColumn(new GridColumn().NoWrap().Width(2).PadRight(1)).AddColumn(new GridColumn().Width(textWidth));
                line.AddRow(new Markup($"[grey]{(last ? "└─" : "├─")}{Repeat(last ? "\n " : "\n│", lines - 1)}[/]"), body[i]);
                grid.AddRow(new Markup($"[grey]│{Repeat("\n│", lines - 1)}[/]"), line, new Text(string.Empty));
            }
        }
        rows.Add(grid);

        var tasks = state.Tasks;
        if (Verbose && !state.IsFinal)
            foreach (var task in tasks.Where(t => t.Status == ThreadTaskStatus.Running))
            {
                var output = state.OutputOf(task.Id).ReplaceLineEndings("\n").Split('\n').Where(l => l.Trim().Length > 0).TakeLast(8);
                var text = string.Join("\n", output);
                if (text.Length > 0)
                    rows.Add(new Panel(new Text(text, new Style(Spectre.Console.Color.Grey))).Header(Markup.Escape(task.TaskKey)).BorderColor(Spectre.Console.Color.Grey));
            }

        if (state.Notice is { } notice) rows.Add(new Markup($"[yellow]{Markup.Escape(notice)}[/]"));
        else if (interactive && !state.IsFinal)
            rows.Add(new Markup($"[grey]{Markup.Escape(Lang.T("Ctrl+C batalkan · Esc lepas (thread lanjut di latar belakang) · v output tugas", "Ctrl+C cancel · Esc detach (the thread goes on in the background) · v task output"))}[/]"));

        return new Rows(rows);
    }

    // Screen lines the renderable takes at that width.
    private static int LinesOf(IRenderable renderable, int width)
    {
        var console = AnsiConsole.Console;
        var segments = renderable.Render(RenderOptions.Create(console, console.Profile.Capabilities), width);
        return Math.Max(1, Segment.SplitLines(segments, width).Count);
    }

    private static string Repeat(string text, int times) => times <= 0 ? string.Empty : string.Concat(Enumerable.Repeat(text, times));

    private static string PhaseTitle(ChatPhase phase, ChatThreadState state)
    {
        var detail = state.Detail;
        switch (phase)
        {
            case ChatPhase.Planning:
                var planner = "🧠 Planner" + (detail?.PlannerModel is { Length: > 0 } model ? " · " + model : string.Empty);
                return state.ToolCount > 0 ? planner + Lang.T($" · {state.ToolCount} alat", $" · {state.ToolCount} tools") : planner;
            case ChatPhase.Executing:
                var planned = state.Tasks.Where(t => !IsFixTask(t)).ToList();
                var finished = planned.Count(t => t.Status is not (ThreadTaskStatus.Pending or ThreadTaskStatus.Running));
                return Lang.T("🚀 Eksekusi", "🚀 Execution") + (planned.Count > 0 ? Lang.T($" · {finished}/{planned.Count} tugas", $" · {finished}/{planned.Count} tasks") : string.Empty);
            case ChatPhase.Verifying:
                return Lang.T("🔍 Verifikasi", "🔍 Verification") + (detail?.ReviewerModel is { Length: > 0 } reviewer ? " · " + reviewer : string.Empty);
            case ChatPhase.Testing:
                return "🧪 Testing" + (detail?.TesterModel is { Length: > 0 } tester ? " · " + tester : string.Empty);
            default:
                return Lang.T("📦 Rangkuman", "📦 Summary") + (detail?.WrapperModel is { Length: > 0 } wrapper ? " · " + wrapper : string.Empty);
        }
    }

    // The lines under a stage: what happens while it runs, a one-line result once it is done.
    private static List<IRenderable> PhaseBody(PhaseInfo phase, ChatThreadState state, int width)
    {
        var lines = new List<IRenderable>();
        if (phase.State == PhaseState.Waiting) return lines;
        var open = phase.State is PhaseState.Running or PhaseState.Stopped;
        var detail = state.Detail;

        switch (phase.Phase)
        {
            case ChatPhase.Planning:
                if (phase.State == PhaseState.Running)
                    foreach (var tool in state.Activity) lines.Add(new Markup($"[grey]{Markup.Escape(OneLine(tool, 140))}[/]"));
                if (!string.IsNullOrWhiteSpace(state.PlanSummary))
                    lines.Add(new Markup($"[grey]{Markup.Escape(Lang.T("Rencana:", "Plan:"))}[/] {Markup.Escape(OneLine(state.PlanSummary, 400))}"));
                AddLogs(lines, state.LogsOf(ChatPhase.Planning), importantOnly: true);
                break;

            case ChatPhase.Executing:
                var planned = state.Tasks.Where(t => !IsFixTask(t)).ToList();
                if (open) lines.AddRange(TaskLines(planned, width));
                else if (planned.Count > 0) lines.Add(new Markup(TaskCounts(planned)));
                if (open) AddLogs(lines, state.LogsOf(ChatPhase.Executing), importantOnly: true);
                break;

            case ChatPhase.Verifying:
                var fixes = state.Tasks.Where(IsFixTask).ToList();
                if (open) lines.AddRange(TaskLines(fixes, width));
                if (open) AddLogs(lines, state.LogsOf(ChatPhase.Verifying), importantOnly: false);
                if (phase.State != PhaseState.Running && detail is not null)
                    AddChecks(lines, Checks(detail).Where(c => c.Name != "Testing"));
                break;

            case ChatPhase.Testing:
                if (open) AddLogs(lines, state.LogsOf(ChatPhase.Testing), importantOnly: false);
                if (phase.State != PhaseState.Running && detail is not null)
                    AddChecks(lines, Checks(detail).Where(c => c.Name == "Testing"));
                break;

            case ChatPhase.Wrapping:
                if (phase.State == PhaseState.Running)
                {
                    lines.Add(new Markup($"[grey]{Markup.Escape(Lang.T("menyusun jawaban …", "writing the answer …"))}[/]"));
                    AddLogs(lines, state.LogsOf(ChatPhase.Wrapping), importantOnly: true);
                }
                break;
        }
        return lines;
    }

    // One line per task (state, key, title, file, node, time) laid out to fit width, the same columns on every line so
    // they line up although every task is its own line of the stepper. A narrow terminal drops the node first, then
    // shortens the path ("src/…/Hosting/Autostart.cs") and the title.
    private static IEnumerable<IRenderable> TaskLines(IReadOnlyList<ThreadTaskDto> tasks, int width)
    {
        if (tasks.Count == 0) yield break;
        const int timeWidth = 6;
        int Widest(Func<ThreadTaskDto, string> text) => Math.Max(1, tasks.Max(t => text(t).Length));
        var (key, titleMax, fileMax, node) = (Widest(t => t.TaskKey), Math.Min(48, Widest(t => t.Title)), Widest(FileOf), Widest(t => t.NodeName ?? string.Empty));

        // Icon and key with their spaces, the time with its space; what is left goes to title, file and node.
        var rest = width - 2 - (key + 1) - (timeWidth + 1);
        var showNode = rest - node - 2 >= titleMax + fileMax + 2 || rest >= 70;
        if (showNode) rest -= node + 2;
        var file = Math.Min(fileMax, Math.Max(12, rest / 2));
        var title = Math.Max(6, Math.Min(titleMax, rest - file - 2));
        file = Math.Max(0, Math.Min(file, rest - title - 2));

        foreach (var task in tasks)
        {
            var line = new System.Text.StringBuilder()
                .Append($"[{TaskColor(task.Status)}]{TaskIcon(task.Status)}[/] ")
                .Append($"[grey]{Markup.Escape(task.TaskKey.PadRight(key))}[/] ")
                .Append(Markup.Escape(OneLine(task.Title, title).PadRight(title)));
            if (file > 0) line.Append("  ").Append($"[grey]{Markup.Escape(ClientProgress.CompactPath(FileOf(task), file).PadRight(file))}[/]");
            if (showNode) line.Append("  ").Append($"[grey]{Markup.Escape((task.NodeName ?? string.Empty).PadRight(node))}[/]");
            line.Append($" [grey]{Markup.Escape((task.DurationMs is { } ms ? Seconds(ms) : string.Empty).PadLeft(timeWidth))}[/]");
            yield return new Markup(line.ToString());
        }
    }

    // "✔ 3 berhasil · ✖ 1 gagal": the tasks of a finished stage by their end state.
    private static string TaskCounts(IReadOnlyList<ThreadTaskDto> tasks) =>
        string.Join(" [grey]·[/] ", tasks.GroupBy(t => t.Status).OrderBy(g => g.Key)
            .Select(g => $"[{TaskColor(g.Key)}]{TaskIcon(g.Key)}[/] {g.Count()} {Markup.Escape(TaskLabel(g.Key))}"));

    // The stage's last log lines; importantOnly: only warnings and errors (stages that log a lot of routine lines).
    private static void AddLogs(List<IRenderable> lines, IReadOnlyList<(ActivityLevel Level, string Message)> logs, bool importantOnly)
    {
        foreach (var (level, message) in logs)
        {
            if (importantOnly && level is not (ActivityLevel.Warning or ActivityLevel.Error)) continue;
            var color = level switch { ActivityLevel.Error => "red", ActivityLevel.Warning => "yellow", ActivityLevel.Success => "green", _ => "grey" };
            lines.Add(new Markup($"[{color}]{Markup.Escape(OneLine(message, 160))}[/]"));
        }
    }

    private static void AddChecks(List<IRenderable> lines, IEnumerable<(string Name, string Outcome, string? Text)> checks)
    {
        foreach (var (name, outcome, text) in checks)
            lines.Add(new Markup($"[{OutcomeColor(outcome)}]{OutcomeIcon(outcome)}[/] {Markup.Escape(name)}: [{OutcomeColor(outcome)}]{Markup.Escape(OutcomeLabel(outcome))}[/]"
                                 + (text is null ? string.Empty : $" [grey]— {Markup.Escape(OneLine(text, 120))}[/]")));
    }

    // A finished stage whose result failed: a task of the plan (execution), a fix task or a check (verification), the
    // app test (testing).
    private static bool PhaseFailed(ChatPhase phase, ChatThreadState state)
    {
        var checks = state.Detail is { } detail ? Checks(detail).ToList() : [];
        return phase switch
        {
            ChatPhase.Executing => state.Tasks.Any(t => !IsFixTask(t) && t.Status == ThreadTaskStatus.Failed),
            ChatPhase.Verifying => state.Tasks.Any(t => IsFixTask(t) && t.Status == ThreadTaskStatus.Failed)
                                   || checks.Any(c => c.Name != "Testing" && c.Outcome == "failed"),
            ChatPhase.Testing => checks.Any(c => c.Name == "Testing" && c.Outcome == "failed"),
            _ => false
        };
    }

    // Fix tasks of the verification (V<round>-<n>) belong to the verification stage, not to the plan.
    private static bool IsFixTask(ThreadTaskDto task) =>
        task.TaskKey.Length > 1 && task.TaskKey[0] == 'V' && char.IsDigit(task.TaskKey[1]) && task.TaskKey.Contains('-');

    private static string OutcomeColor(string outcome) => outcome switch { "passed" => "green", "failed" => "red", _ => "yellow" };

    private static string OutcomeIcon(string outcome) => outcome switch { "passed" => "✔", "failed" => "✖", _ => "↷" };

    public static string OutcomeLabel(string outcome) => outcome switch
    {
        "passed" => Lang.T("lolos", "passed"),
        "failed" => Lang.T("gagal", "failed"),
        "skipped" => Lang.T("dilewati", "skipped"),
        _ => outcome
    };

    // One change as a plain line.
    public void Plain(ThreadChange change)
    {
        switch (change)
        {
            case ThreadChange.Status status:
                Console.Out.WriteLine($"[{StatusLabel(status.Value)}]{(status.PlanSummary is { } summary ? " " + OneLine(summary, 300) : string.Empty)}");
                break;
            case ThreadChange.Tool tool:
                Console.Out.WriteLine($"  · {tool.Name} {OneLine(tool.Detail, 200)}".TrimEnd());
                break;
            case ThreadChange.TaskChanged { Value: var task }:
                var time = task.DurationMs is { } ms ? " " + Seconds(ms) : string.Empty;
                Console.Out.WriteLine($"  {TaskIcon(task.Status)} {task.TaskKey} {TaskLabel(task.Status)}: {OneLine(task.Title, 120)} ({FileOf(task)}){time}");
                break;
            case ThreadChange.Output output when Verbose:
                foreach (var line in output.Text.ReplaceLineEndings("\n").Split('\n').Where(l => l.Trim().Length > 0))
                    Console.Out.WriteLine($"    {output.Task?.TaskKey ?? "-"} │ {line}");
                break;
            case ThreadChange.Git git:
                Console.Out.WriteLine($"  git: {git.State}");
                break;
            case ThreadChange.Log log:
                Console.Out.WriteLine($"  · {OneLine(log.Message, 300)}");
                break;
        }
    }

    // The end of a thread: the answer, the tasks with their files, the checks and the link to the thread page.
    public void Summary(ThreadDetailDto detail, string url)
    {
        var status = detail.Summary.Status;
        var answer = detail.FinalResponse?.Trim();
        var checks = Checks(detail).ToList();

        if (!Rich)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine($"== {StatusLabel(status)} ==");
            if (!string.IsNullOrEmpty(answer)) Console.Out.WriteLine(answer);
            if (detail.Tasks.Count > 0)
            {
                Console.Out.WriteLine();
                foreach (var task in detail.Tasks.OrderBy(t => t.Order))
                    Console.Out.WriteLine($"  {TaskIcon(task.Status)} {task.Action,-7} {FileOf(task)}{(task.Error is { Length: > 0 } error ? " — " + OneLine(error, 200) : string.Empty)}");
            }
            foreach (var (name, outcome, text) in checks) Console.Out.WriteLine($"  {name}: {outcome}{(text is null ? string.Empty : " — " + text)}");
            if (!string.IsNullOrWhiteSpace(detail.Error)) Console.Error.WriteLine(detail.Error);
            Console.Out.WriteLine(url);
            return;
        }

        // Status, total time and tokens in the rule; then the answer drawn from its Markdown.
        var stats = new List<string>();
        if (detail.Summary.TotalMs is { } total) stats.Add(Seconds(total));
        if (detail.Summary.TotalTokens > 0) stats.Add(Lang.T($"{Tokens(detail.Summary.TotalTokens)} token", $"{Tokens(detail.Summary.TotalTokens)} tokens"));
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[{StatusColor(status)}]{Markup.Escape(Icon(status) + " " + StatusLabel(status))}[/]"
                                   + (stats.Count > 0 ? $" [grey]· {Markup.Escape(string.Join(" · ", stats))}[/]" : string.Empty)).LeftJustified().RuleStyle("grey"));
        if (!string.IsNullOrEmpty(answer))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(TerminalMarkdown.Render(answer));
        }

        if (detail.Tasks.Count > 0)
        {
            AnsiConsole.WriteLine();
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Spectre.Console.Color.Grey)
                .AddColumn(new TableColumn(string.Empty).Centered())
                .AddColumn(Lang.T("Aksi", "Action"))
                .AddColumn("File")
                .AddColumn(new TableColumn(Lang.T("Waktu", "Time")).RightAligned())
                .AddColumn(Lang.T("Catatan", "Note"));
            foreach (var task in detail.Tasks.OrderBy(t => t.Order))
                table.AddRow(
                    new Markup($"[{TaskColor(task.Status)}]{TaskIcon(task.Status)}[/]"),
                    new Text(task.Action),
                    new Text(FileOf(task)),
                    new Markup($"[grey]{(task.DurationMs is { } ms ? Seconds(ms) : string.Empty)}[/]"),
                    new Markup(task.Error is { Length: > 0 } error ? $"[red]{Markup.Escape(OneLine(error, 120))}[/]" : $"[grey]{Markup.Escape(OneLine(task.Summary ?? string.Empty, 120))}[/]"));
            AnsiConsole.Write(table);
        }

        if (checks.Count > 0)
        {
            var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(1)).AddColumn(new GridColumn().NoWrap().PadRight(2))
                .AddColumn(new GridColumn().NoWrap().PadRight(2)).AddColumn();
            foreach (var (name, outcome, text) in checks)
                grid.AddRow(
                    new Markup($"[{OutcomeColor(outcome)}]{OutcomeIcon(outcome)}[/]"),
                    new Text(name),
                    new Markup($"[{OutcomeColor(outcome)}]{Markup.Escape(OutcomeLabel(outcome))}[/]"),
                    new Markup($"[grey]{Markup.Escape(text ?? string.Empty)}[/]"));
            AnsiConsole.Write(new Padder(grid, new Padding(1, 1, 0, 0)));
        }

        if (!string.IsNullOrWhiteSpace(detail.Error))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]✖ {Markup.Escape(detail.Error)}[/]");
        }

        // The models that worked on the thread, then the link to its page.
        var models = new List<string>();
        if (detail.PlannerModel is { Length: > 0 } planner) models.Add($"planner {planner}");
        if (detail.Tasks.Select(t => t.Model).Where(m => !string.IsNullOrEmpty(m)).Distinct().ToList() is { Count: > 0 } executors)
            models.Add($"executor {string.Join(", ", executors)}");
        if (detail.ReviewerModel is { Length: > 0 } reviewer) models.Add($"reviewer {reviewer}");
        if (detail.TesterModel is { Length: > 0 } tester) models.Add($"tester {tester}");
        if (detail.WrapperModel is { Length: > 0 } wrapper) models.Add($"wrapper {wrapper}");
        AnsiConsole.WriteLine();
        if (models.Count > 0) AnsiConsole.MarkupLine($"[grey]🤖 {Markup.Escape(string.Join(" · ", models))}[/]");
        AnsiConsole.MarkupLine($"[grey]🔗 [link={url}]{Markup.Escape(url)}[/][/]");
        AnsiConsole.WriteLine();
    }

    // The permission card: what is asked, built from the request's parts. approval.Text is the reply written for a chat
    // (its own title, numbered options to reply with), which would repeat the card; the choices are the list of
    // ApprovalOptions the session draws below it.
    public void Approval(PendingApprovalDto approval, string url)
    {
        var title = Lang.T("🔐 AIT minta izin", "🔐 AIT asks for permission");

        if (!Rich)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(title);
            if (!string.IsNullOrWhiteSpace(approval.Reason)) Console.Out.WriteLine(Inline(approval.Reason.Trim(), markup: false));
            foreach (var item in approval.Items)
                Console.Out.WriteLine($"- {ApprovalRequest.KindLabel(item.Kind)}: {item.Target}{(item.Detail is { Length: > 0 } d ? " — " + d.Trim() : string.Empty)}");
            Console.Out.WriteLine(url);
            return;
        }

        var body = new List<IRenderable>();
        if (!string.IsNullOrWhiteSpace(approval.Reason)) body.Add(new Markup(Inline(approval.Reason.Trim(), markup: true)));
        foreach (var item in approval.Items)
        {
            body.Add(new Text(string.Empty));
            body.Add(new Markup($"[bold]{Markup.Escape(ApprovalRequest.KindLabel(item.Kind))}[/]"));
            body.Add(new Padder(new Markup($"[cyan]{Markup.Escape(item.Target)}[/]"), new Padding(2, 0, 0, 0)));
            if (item.Detail is { Length: > 0 } detail) body.Add(new Padder(new Markup($"[grey]{Markup.Escape(detail.Trim())}[/]"), new Padding(2, 0, 0, 0)));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Rows(body)).Header(Markup.Escape(title)).BorderColor(Spectre.Console.Color.Yellow).Padding(1, 0));
    }

    // The choices of a permission request in the order of the chat card, each with its key and the number of that card
    // (1-5), which both answer it directly.
    public static IReadOnlyList<ApprovalOption> ApprovalOptions(PendingApprovalDto approval)
    {
        var options = new List<ApprovalOption> { new('y', '1', ApprovalDecision.Once, Lang.T("**Izinkan sekali** — hanya untuk aksi ini", "**Allow once** — for this action only")) };
        if (approval.AllowAlways)
            options.Add(new('a', '2', ApprovalDecision.Always, Lang.T("**Izinkan selalu** — disimpan di mesin ini untuk project ini (cabut di Project → Detail → Izin AIT)",
                "**Always allow** — saved on this machine for this project (revoke in Project → Detail → AIT permissions)")));
        if (approval.AllowPattern)
        {
            var patterns = string.Join(", ", approval.Items.Where(i => i.Pattern is not null).Select(i => $"`{i.Pattern}`").Distinct());
            options.Add(new('p', '4', ApprovalDecision.AlwaysPattern, Lang.T($"**Izinkan selalu untuk pola** {patterns} — perintah lain yang diawali kata yang sama juga diizinkan",
                $"**Always allow the pattern** {patterns} — other commands starting with the same words are allowed too")));
        }
        if (approval.AllowAll)
            options.Add(new('s', '5', ApprovalDecision.AlwaysAll, Lang.T("**Izinkan semua perintah di project ini** — termasuk hapus/push/publish (file kredensial tetap ditolak)",
                "**Allow all commands in this project** — deleting/pushing/publishing included (credential files stay refused)")));
        options.Add(new('n', '3', ApprovalDecision.Deny, Lang.T("**Tolak**", "**Deny**")));
        return options;
    }

    // Inline Markdown (**bold**, `code`, *italic*, [text](url)) as Spectre markup, or as plain text without the marks
    // (a link keeps its url in parentheses).
    public static string Inline(string markdown, bool markup)
    {
        var result = new System.Text.StringBuilder();
        var last = 0;
        foreach (System.Text.RegularExpressions.Match match in InlineMarks().Matches(markdown))
        {
            result.Append(markup ? Markup.Escape(markdown[last..match.Index]) : markdown[last..match.Index]);
            var groups = match.Groups;
            if (groups["bold"].Success) result.Append(markup ? $"[bold]{Markup.Escape(groups["bold"].Value)}[/]" : groups["bold"].Value);
            else if (groups["code"].Success) result.Append(markup ? $"[cyan]{Markup.Escape(groups["code"].Value)}[/]" : groups["code"].Value);
            else if (groups["italic"].Success) result.Append(markup ? $"[italic]{Markup.Escape(groups["italic"].Value)}[/]" : groups["italic"].Value);
            else
                result.Append(markup
                    ? $"[link={Markup.Escape(groups["url"].Value)}]{Markup.Escape(groups["text"].Value)}[/]"
                    : $"{groups["text"].Value} ({groups["url"].Value})");
            last = match.Index + match.Length;
        }
        result.Append(markup ? Markup.Escape(markdown[last..]) : markdown[last..]);
        return result.ToString();
    }

    // Italic needs a non-space right inside both stars and no word character outside them ("2 * 3 * 4" stays text).
    [System.Text.RegularExpressions.GeneratedRegex(@"\*\*(?<bold>[^*\n]+?)\*\*|`(?<code>[^`\n]+)`|\[(?<text>[^\]\n]+)\]\((?<url>[^)\s]+)\)|(?<![\w*])\*(?<italic>[^*\s](?:[^*\n]*[^*\s])?)\*(?![\w*])")]
    private static partial System.Text.RegularExpressions.Regex InlineMarks();

    // Build/review, unit tests and Auto Testing from the thread's reports (outcome, plus a short note).
    public static IEnumerable<(string Name, string Outcome, string? Text)> Checks(ThreadDetailDto detail)
    {
        if (Report(detail.VerificationJson) is { } verification && Outcome(verification) is { } verified)
            yield return (Lang.T("Verifikasi", "Verification"), verified, Field(verification, "command"));

        if (Report(detail.UnitTestJson) is { } unit && Outcome(unit) is { } unitOutcome)
        {
            string? counts = null;
            if (unit.TryGetProperty("tests", out var tests) && tests.ValueKind == JsonValueKind.Array && tests.GetArrayLength() > 0)
                counts = string.Join(", ", tests.EnumerateArray()
                    .Select(t => ChatThreadState.Text(t, "verdict") ?? "?")
                    .GroupBy(v => v)
                    .Select(g => $"{g.Count()} {OutcomeLabel(g.Key)}"));
            yield return (Lang.T("Unit test", "Unit tests"), unitOutcome, counts ?? Field(unit, "summary"));
        }

        if (Report(detail.TestingJson) is { } testing && Outcome(testing) is { } tested)
            yield return ("Testing", tested, Field(testing, "summary"));
    }

    private static JsonElement? Report(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Outcome(JsonElement report) => ChatThreadState.Text(report, "outcome");

    private static string? Field(JsonElement report, string name) =>
        ChatThreadState.Text(report, name) is { Length: > 0 } value ? OneLine(value, 160) : null;

    public static string StatusLabel(ThreadStatus status) => status switch
    {
        ThreadStatus.Pending => Lang.T("Menunggu giliran", "Pending"),
        ThreadStatus.Planning => Lang.T("Merencanakan", "Planning"),
        ThreadStatus.Executing => Lang.T("Mengerjakan tugas", "Running tasks"),
        ThreadStatus.Verifying => Lang.T("Verifikasi (build, review, unit test)", "Verifying (build, review, unit tests)"),
        ThreadStatus.Testing => Lang.T("Testing aplikasi", "Testing the app"),
        ThreadStatus.Wrapping => Lang.T("Menyusun jawaban", "Writing the answer"),
        ThreadStatus.Completed => Lang.T("Selesai", "Completed"),
        ThreadStatus.Failed => Lang.T("Gagal", "Failed"),
        ThreadStatus.Cancelled => Lang.T("Dibatalkan", "Cancelled"),
        ThreadStatus.AwaitingInput => Lang.T("Menunggu developer", "Waiting for the developer"),
        ThreadStatus.RolledBack => Lang.T("Perubahan dibatalkan (rollback)", "Rolled back"),
        _ => status.ToString()
    };

    private static string TaskLabel(ThreadTaskStatus status) => status switch
    {
        ThreadTaskStatus.Pending => Lang.T("menunggu", "pending"),
        ThreadTaskStatus.Running => Lang.T("berjalan", "running"),
        ThreadTaskStatus.Succeeded => Lang.T("berhasil", "succeeded"),
        ThreadTaskStatus.Failed => Lang.T("gagal", "failed"),
        ThreadTaskStatus.Skipped => Lang.T("dilewati", "skipped"),
        ThreadTaskStatus.Cancelled => Lang.T("dibatalkan", "cancelled"),
        _ => status.ToString()
    };

    private static string Icon(ThreadStatus status) => status switch
    {
        ThreadStatus.Completed => "✔",
        ThreadStatus.Failed => "✖",
        ThreadStatus.Cancelled or ThreadStatus.RolledBack => "⏹",
        ThreadStatus.AwaitingInput => "⏸",
        _ => "•"
    };

    private static string StatusColor(ThreadStatus status) => status switch
    {
        ThreadStatus.Completed => "green",
        ThreadStatus.Failed => "red",
        ThreadStatus.Cancelled or ThreadStatus.RolledBack or ThreadStatus.AwaitingInput => "yellow",
        _ => "cyan"
    };

    private static string TaskIcon(ThreadTaskStatus status) => status switch
    {
        ThreadTaskStatus.Pending => "·",
        ThreadTaskStatus.Running => "▶",
        ThreadTaskStatus.Succeeded => "✔",
        ThreadTaskStatus.Failed => "✖",
        ThreadTaskStatus.Skipped => "↷",
        ThreadTaskStatus.Cancelled => "⏹",
        _ => "?"
    };

    private static string TaskColor(ThreadTaskStatus status) => status switch
    {
        ThreadTaskStatus.Running => "cyan",
        ThreadTaskStatus.Succeeded => "green",
        ThreadTaskStatus.Failed => "red",
        ThreadTaskStatus.Skipped or ThreadTaskStatus.Cancelled => "yellow",
        _ => "grey"
    };

    private static string FileOf(ThreadTaskDto task) =>
        task.TargetPath is { Length: > 0 } target ? $"{task.FilePath} → {target}" : task.FilePath;

    // "950", "12.3k", "1.2M".
    public static string Tokens(int tokens) => tokens switch
    {
        >= 1_000_000 => FormattableString.Invariant($"{tokens / 1_000_000d:0.0}M"),
        >= 1_000 => FormattableString.Invariant($"{tokens / 1_000d:0.0}k"),
        _ => tokens.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    private static string Seconds(long ms) => ms < 60_000 ? FormattableString.Invariant($"{ms / 1000d:0.0}s") : $"{ms / 60_000}m{ms / 1000 % 60:00}s";

    private static string OneLine(string text, int max)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= max ? line : line[..(max - 1)] + "…";
    }
}
