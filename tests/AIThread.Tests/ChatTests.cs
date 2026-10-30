using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using AIThread.API.Controllers;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Images;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.UI.Hosting.Chat;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AIThread.Tests;

public class ChatTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];
    private static readonly byte[] Bmp = [(byte)'B', (byte)'M', 0, 0, 0, 0];

    private static ChatImage Image(string name = "a.png") => new(name, "image/png", Png);

    // ---- SSE ----

    [Fact]
    public async Task Sse_events_are_read_with_type_payload_and_thread()
    {
        var threadId = Guid.NewGuid();
        var stream = $$"""
            : comment
            event: thread.updated
            data: {"data":{"id":"{{threadId}}","status":"Executing"},"threadId":"{{threadId}}","timestamp":"2026-10-29T10:00:00Z"}

            event: ping
            data: {"data":null,"threadId":null,"timestamp":"2026-10-29T10:00:01Z"}

            event: task.output
            data: {"data":{"text":"a",
            data: "taskId":"x"},"threadId":"{{threadId}}","timestamp":"2026-10-29T10:00:02Z"}
            """;

        var events = new List<AppEvent>();
        await foreach (var evt in ChatApiClient.ReadEventsAsync(new StringReader(stream), CancellationToken.None)) events.Add(evt);

        Assert.Equal(["thread.updated", "ping", "task.output"], events.Select(e => e.Type));
        Assert.Equal(threadId, events[0].ThreadId);
        Assert.Equal("Executing", ((JsonElement)events[0].Data!).GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, ((JsonElement)events[1].Data!).ValueKind);
        // Data lines are joined with a newline; the last event needs no blank line before the end.
        Assert.Equal("x", ((JsonElement)events[2].Data!).GetProperty("taskId").GetString());
    }

    [Fact]
    public async Task Sse_data_that_is_not_json_keeps_the_event_without_data()
    {
        var events = new List<AppEvent>();
        await foreach (var evt in ChatApiClient.ReadEventsAsync(new StringReader("event: thread.updated\ndata: not json\n\n"), CancellationToken.None)) events.Add(evt);

        var only = Assert.Single(events);
        Assert.Equal("thread.updated", only.Type);
        Assert.Null(only.Data);
    }

    [Fact]
    public void Thread_state_follows_status_tasks_tools_and_approvals()
    {
        var threadId = Guid.NewGuid();
        var state = new ChatThreadState(threadId);
        AppEvent Event(string type, object data, Guid? thread = null) =>
            new(type, JsonSerializer.SerializeToElement(data, ChatApiClient.Json), thread ?? threadId, DateTime.UtcNow);

        Assert.IsType<ThreadChange.Status>(state.Apply(Event(AppEventTypes.ThreadUpdated, new { Id = threadId, Status = "Planning" })));
        Assert.False(state.Moved);
        Assert.IsType<ThreadChange.Tool>(state.Apply(Event(AppEventTypes.ThreadOutput, new { kind = "planner.tool", tool = "read_file", detail = "src/A.cs" })));
        var task = new ThreadTaskDto(Guid.NewGuid(), "T1", 1, "Fix", "src/A.cs", "edit", null, "", null, ThreadTaskStatus.Running, "node", null, 1, null, null, null, 0, null, null, null);
        Assert.IsType<ThreadChange.TaskChanged>(state.Apply(Event(AppEventTypes.TaskUpdated, task)));
        Assert.Null(state.Apply(Event(AppEventTypes.TaskUpdated, task)));
        Assert.Null(state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Completed" }, Guid.NewGuid())));
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "AwaitingInput" }));
        var next = Guid.NewGuid();
        var answered = Assert.IsType<ThreadChange.ApprovalAnswered>(state.Apply(Event(AppEventTypes.ApprovalChanged, new { ThreadId = threadId, NextThreadId = next })));

        Assert.Equal(next, answered.NextThreadId);
        Assert.True(state.IsFinal);
        Assert.True(state.Moved);
        Assert.Equal(["read_file · src/A.cs"], state.Activity);
        Assert.Single(state.Tasks);
    }

    // ---- Stages and rendering ----

    private static ThreadTaskDto TaskOf(string key, ThreadTaskStatus status, long? ms = null) =>
        new(Guid.NewGuid(), key, int.Parse(key[^1..]), "Change " + key, $"src/{key}.cs", "edit", null, "", null, status, "exec-1", "model-x", 1, "done", null, null, 0, null, null, ms);

    private static ThreadDetailDto Detail(ThreadStatus status, IReadOnlyList<ThreadTaskDto> tasks, string? answer = null, long? planningMs = null,
        long? verificationMs = null, string? verificationJson = null) =>
        new(new ThreadSummaryDto(Guid.NewGuid(), null, "P", ThreadSource.Dashboard, null, "prompt", status, "Change two files", tasks.Count, 0, 0, 45_200, 3_000, 3_000, 23_456,
                DateTime.UtcNow, null),
            null, answer, null, "planner-x", "wrapper-x", 3, 0, 0, 0, planningMs, null, null, tasks,
            VerificationMs: verificationMs, VerificationJson: verificationJson);

    private static string Render(IRenderable renderable)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
            Interactive = InteractionSupport.No
        });
        console.Profile.Width = 120;
        console.Write(renderable);
        return writer.ToString();
    }

    [Fact]
    public void Stages_follow_the_status_and_fold_when_the_thread_ends()
    {
        using var language = Lang.Use("en");
        var threadId = Guid.NewGuid();
        var state = new ChatThreadState(threadId);
        AppEvent Event(string type, object data) => new(type, JsonSerializer.SerializeToElement(data, ChatApiClient.Json), threadId, DateTime.UtcNow);

        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Planning" }));
        state.Apply(Event(AppEventTypes.ThreadOutput, new { kind = "planner.tool", tool = "read_file", detail = "src/A.cs" }));
        Assert.Equal([PhaseState.Running, PhaseState.Waiting, PhaseState.Waiting, PhaseState.Waiting, PhaseState.Waiting], state.Phases().Select(p => p.State));
        Assert.Equal(1, state.ToolCount);

        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Executing", PlanSummary = "Change two files" }));
        state.Apply(Event(AppEventTypes.Log, new { Level = "Warning", Message = "Executor retried T2" }));
        state.Apply(Event(AppEventTypes.TaskUpdated, TaskOf("T1", ThreadTaskStatus.Succeeded, 2100)));
        state.Apply(Event(AppEventTypes.TaskUpdated, TaskOf("T2", ThreadTaskStatus.Running)));
        var running = Render(new ChatRenderer(rich: true).Progress(state, interactive: true));
        Assert.Contains("🧠 Planner · 1 tools", running);
        Assert.Contains("Plan: Change two files", running);
        Assert.Contains("🚀 Execution · 1/2 tasks", running);
        Assert.Contains("src/T2.cs", running);
        Assert.Contains("Executor retried T2", running);
        Assert.Contains("· 🧪 Testing", running);

        // Testing never ran: it is left out once the thread passed it.
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Verifying" }));
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Wrapping" }));
        state.Apply(Detail(ThreadStatus.Completed, [TaskOf("T1", ThreadTaskStatus.Succeeded, 2100), TaskOf("T2", ThreadTaskStatus.Succeeded, 900)],
            planningMs: 12_300, verificationMs: 4_000, verificationJson: """{"outcome":"passed","command":"dotnet build"}"""));

        var phases = state.Phases();
        Assert.Equal([ChatPhase.Planning, ChatPhase.Executing, ChatPhase.Verifying, ChatPhase.Wrapping], phases.Select(p => p.Phase));
        Assert.All(phases, p => Assert.Equal(PhaseState.Done, p.State));
        Assert.Equal(12_300, phases[0].Ms);
        Assert.Equal(3_000, phases[1].Ms);

        var done = Render(new ChatRenderer(rich: true).Progress(state, interactive: false));
        Assert.Contains("✔ 2 succeeded", done);
        Assert.Contains("Verification: passed", done);
        Assert.Contains("12.3s", done);
        Assert.DoesNotContain("src/T2.cs", done);
        Assert.DoesNotContain("Testing", done);
    }

    [Fact]
    public void Stage_where_a_thread_failed_is_marked_and_stays_open()
    {
        var state = new ChatThreadState(Guid.NewGuid());
        state.Apply(Detail(ThreadStatus.Executing, [TaskOf("T1", ThreadTaskStatus.Running)], planningMs: 5_000));
        state.Apply(Detail(ThreadStatus.Failed, [TaskOf("T1", ThreadTaskStatus.Failed, 800)], planningMs: 5_000));

        Assert.Equal([PhaseState.Done, PhaseState.Stopped], state.Phases().Select(p => p.State));
        Assert.Contains("src/T1.cs", Render(new ChatRenderer(rich: true).Progress(state, interactive: false)));
    }

    [Fact]
    public void Failed_task_marks_the_execution_not_the_summary_and_late_logs_stay_out()
    {
        using var language = Lang.Use("en");
        var threadId = Guid.NewGuid();
        var state = new ChatThreadState(threadId);
        AppEvent Event(string type, object data) => new(type, JsonSerializer.SerializeToElement(data, ChatApiClient.Json), threadId, DateTime.UtcNow);

        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Planning" }));
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Executing" }));
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Wrapping" }));
        state.Apply(Detail(ThreadStatus.Failed, [TaskOf("T1", ThreadTaskStatus.Failed, 32_600)], planningMs: 29_700));
        state.Apply(Event(AppEventTypes.Log, new { Level = "Error", Message = "Thread finished: 0/1 tasks succeeded in 67.1s." }));

        Assert.Equal([PhaseState.Done, PhaseState.Done, PhaseState.Done], state.Phases().Select(p => p.State));
        Assert.Empty(state.LogsOf(ChatPhase.Planning));
        var text = Render(new ChatRenderer(rich: true).Progress(state, interactive: false));
        Assert.Contains("✖ 🚀 Execution", text);
        Assert.Contains("✔ 📦 Summary", text);
        Assert.Contains("✔ 🧠 Planner", text);
        Assert.DoesNotContain("Thread finished", text);
    }

    [Fact]
    public void Rail_runs_beside_every_line_of_a_wrapped_entry()
    {
        using var language = Lang.Use("en");
        var threadId = Guid.NewGuid();
        var state = new ChatThreadState(threadId);
        AppEvent Event(string type, object data) => new(type, JsonSerializer.SerializeToElement(data, ChatApiClient.Json), threadId, DateTime.UtcNow);
        var plan = string.Join(" ", Enumerable.Repeat("The value name in the Run key changes from AIThread to AIT and the old one is removed.", 4));

        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Planning" }));
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Executing", PlanSummary = plan }));
        state.Apply(Event(AppEventTypes.TaskUpdated, TaskOf("T1", ThreadTaskStatus.Failed, 6_200)));
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Failed" }));

        var lines = Render(new ChatRenderer(rich: true).Progress(state, interactive: false)).ReplaceLineEndings("\n").Split('\n');
        var planner = Array.FindIndex(lines, l => l.Contains("🧠 Planner"));
        var execution = Array.FindIndex(lines, l => l.Contains("🚀 Execution"));
        Assert.True(execution - planner > 2, string.Join("\n", lines));
        // Every screen line of the wrapped plan has the rail; the first carries the branch, the others none under └─.
        Assert.All(lines[(planner + 1)..execution], l => Assert.StartsWith("│ ", l));
        Assert.StartsWith("│ └─ Plan:", lines[planner + 1]);
        Assert.All(lines[(planner + 2)..execution], l => Assert.StartsWith("│    ", l));
    }

    [Fact]
    public void Task_lines_fit_a_narrow_terminal()
    {
        using var language = Lang.Use("en");
        var threadId = Guid.NewGuid();
        var state = new ChatThreadState(threadId);
        AppEvent Event(string type, object data) => new(type, JsonSerializer.SerializeToElement(data, ChatApiClient.Json), threadId, DateTime.UtcNow);
        state.Apply(Event(AppEventTypes.ThreadUpdated, new { Status = "Executing" }));
        state.Apply(Event(AppEventTypes.TaskUpdated,
            new ThreadTaskDto(Guid.NewGuid(), "T1", 1, "Rename the autostart value", "src/AIThread.UI/Hosting/Autostart.cs", "edit", null, "", null,
                ThreadTaskStatus.Running, "Executor", null, 1, null, null, null, 0, null, null, null)));

        var writer = new StringWriter();
        var narrow = AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Out = new AnsiConsoleOutput(writer) });
        narrow.Profile.Width = 60;
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = narrow;
        try
        {
            narrow.Write(new ChatRenderer(rich: true).Progress(state, interactive: false));
        }
        finally
        {
            AnsiConsole.Console = previous;
        }

        var line = writer.ToString().ReplaceLineEndings("\n").Split('\n').Single(l => l.Contains("T1"));
        Assert.Contains("Rename", line);
        Assert.Contains("Autostart.cs", line);
        Assert.DoesNotContain("Executor", line);
        Assert.True(line.TrimEnd().Length <= 60, line);
    }

    [Fact]
    public void Answer_markdown_is_drawn_for_the_terminal()
    {
        const string answer = """
            ## Changes

            - Added **retry** to `Client.cs`
              1. nested step
            > note

            | File | What |
            |---|---|
            | `A.cs` | edit |

            ```csharp
            var x = 1;
            ```
            ---
            See [docs](https://example.com) and *this*.
            """;
        var text = Render(TerminalMarkdown.Render(answer));
        Assert.Contains("Changes", text);
        Assert.DoesNotContain("##", text);
        Assert.Contains("• Added retry to Client.cs", text);
        Assert.Contains("1. nested step", text);
        Assert.Contains("▌ note", text);
        Assert.Contains("│ A.cs │ edit │", text);
        Assert.Contains("var x = 1;", text);
        Assert.Contains("csharp", text);
        Assert.DoesNotContain("```", text);
        Assert.DoesNotContain("|---|", text);
        Assert.Contains("See docs and this.", text);
    }

    // ---- Project resolution ----

    private static ProjectDto Project(string name, string root) =>
        new(Guid.NewGuid(), name, root, [], null, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0, true, "key", default, null);

    [Fact]
    public void Project_is_found_by_name_by_folder_and_by_the_current_directory()
    {
        using var workspace = new TempWorkspace();
        var outer = Path.Combine(workspace.Root, "repo");
        var inner = Path.Combine(outer, "web");
        var sibling = Path.Combine(workspace.Root, "repo-tools");
        foreach (var folder in new[] { Path.Combine(inner, "src"), sibling }) Directory.CreateDirectory(folder);
        var projects = new[] { Project("Repo", outer), Project("Web", inner), Project("Tools", sibling) };

        Assert.Equal("Web", ProjectResolver.Resolve(projects, "web", workspace.Root).Project!.Name);
        Assert.Equal("Web", ProjectResolver.Resolve(projects, null, Path.Combine(inner, "src")).Project!.Name);
        Assert.Equal("Repo", ProjectResolver.Resolve(projects, outer, workspace.Root).Project!.Name);
        Assert.Equal("Tools", ProjectResolver.Resolve(projects, "repo-tools", workspace.Root).Project!.Name);
        Assert.Null(ProjectResolver.Resolve(projects, null, workspace.Root).Project);
        Assert.Null(ProjectResolver.Resolve(projects, null, workspace.Root).Error);
    }

    [Fact]
    public void Unknown_or_ambiguous_project_is_an_error()
    {
        using var language = Lang.Use("en");
        var projects = new[] { Project("App", @"C:\a"), Project("app", @"C:\b") };

        Assert.Contains("2 projects named", ProjectResolver.Resolve(projects, "APP", @"C:\").Error);
        var missing = ProjectResolver.Resolve(projects, "nothing-here", @"C:\");
        Assert.Null(missing.Project);
        Assert.Contains("was not found", missing.Error);
    }

    // ---- Image rules ----

    [Fact]
    public void Media_type_comes_from_the_signature()
    {
        Assert.Equal("image/png", PromptImages.DetectMediaType(Png));
        Assert.Equal("image/jpeg", PromptImages.DetectMediaType(Jpeg));
        Assert.Equal("image/gif", PromptImages.DetectMediaType("GIF89a...."u8));
        Assert.Equal("image/webp", PromptImages.DetectMediaType("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(PromptImages.DetectMediaType(Bmp));
        Assert.Null(PromptImages.DetectMediaType("RIFF\0\0\0\0WAVEfmt "u8));
    }

    [Fact]
    public void Api_validation_checks_count_size_base64_signature_and_media_type()
    {
        using var language = Lang.Use("en");
        ImageAttachmentDto Dto(byte[] data, string type = "image/png") => new(type, Convert.ToBase64String(data));

        Assert.Null(PromptImages.Validate(null));
        Assert.Null(PromptImages.Validate([Dto(Png), Dto(Jpeg, "image/jpeg")]));
        Assert.Contains("At most 6", PromptImages.Validate(Enumerable.Repeat(Dto(Png), 7).ToList()));
        Assert.Contains("not valid base64", PromptImages.Validate([new ImageAttachmentDto("image/png", "@@@")]));
        Assert.Contains("not a png", PromptImages.Validate([Dto(Bmp, "image/bmp")]));
        Assert.Contains("does not match", PromptImages.Validate([Dto(Jpeg, "image/png")]));

        var large = new byte[PromptImages.MaxBytes + 1];
        Png.CopyTo(large, 0);
        Assert.Contains("#1 is too large", PromptImages.Validate([Dto(large)]));
    }

    [Fact]
    public async Task Start_refuses_invalid_images_and_passes_valid_ones_in_order()
    {
        using var language = Lang.Use("en");
        var orchestrator = DispatchProxy.Create<IThreadOrchestrator, RecordingOrchestrator>();
        var recorder = (RecordingOrchestrator)(object)orchestrator;
        var controller = new ThreadsController(null!, orchestrator, null!, null!, null!, null!, null!);
        var projectId = Guid.NewGuid();

        var refused = await Assert.ThrowsAsync<AppValidationException>(() => controller.Start(
            new StartThreadRequest(projectId, "look [Image #1]", [new ImageAttachmentDto("image/bmp", Convert.ToBase64String(Bmp))]), CancellationToken.None));
        Assert.Contains("not a png", refused.Message);
        Assert.Null(recorder.Request);

        await controller.Start(new StartThreadRequest(projectId, " [Image #1] then [Image #2] ",
            [new ImageAttachmentDto("image/jpeg", Convert.ToBase64String(Jpeg)), new ImageAttachmentDto("IMAGE/PNG", Convert.ToBase64String(Png))]), CancellationToken.None);
        var request = recorder.Request!;
        Assert.Equal(ThreadSource.Dashboard, request.Source);
        Assert.Equal("[Image #1] then [Image #2]", request.Prompt);
        Assert.Equal(["image/jpeg", "image/png"], request.Images!.Select(i => i.MediaType));

        await controller.Start(new StartThreadRequest(projectId, "no images"), CancellationToken.None);
        Assert.Null(recorder.Request!.Images);
    }

    public class RecordingOrchestrator : DispatchProxy
    {
        public ThreadRequest? Request { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IThreadOrchestrator.StartInBackgroundAsync)) throw new NotSupportedException(targetMethod?.Name);
            Request = (ThreadRequest)args![0]!;
            return Task.FromResult(Guid.NewGuid());
        }
    }

    // ---- Attachments ----

    [Fact]
    public void Markers_are_numbered_again_in_order_of_appearance()
    {
        var (text, order) = ImageAttachments.Renumber("see [Image #3], [Image #1] and [Image #3] or [Image #9]", new HashSet<int> { 1, 3 });

        Assert.Equal("see [Image #1], [Image #2] and [Image #1] or [Image #9]", text);
        Assert.Equal([3, 1], order);
    }

    [Fact]
    public void Deleted_markers_drop_their_image_and_numbers_are_reused()
    {
        var attachments = new ImageAttachments();
        var text = "a " + attachments.Add(Image("one.png"), "a ", out _);
        text += " b " + attachments.Add(Image("two.png"), text + " b ", out _);
        Assert.Equal("a [Image #1] b [Image #2]", text);

        text = text.Replace("[Image #1]", string.Empty);
        Assert.Equal(["two.png"], attachments.Active(text).Select(a => a.Image.Name));
        Assert.Equal("[Image #1]", attachments.Add(Image("three.png"), text, out _));

        var (sent, images) = attachments.Build("x [Image #2] y [Image #1]");
        Assert.Equal("x [Image #1] y [Image #2]", sent);
        Assert.Equal(2, images.Count);
    }

    [Fact]
    public void A_seventh_image_is_refused()
    {
        using var language = Lang.Use("en");
        var attachments = new ImageAttachments();
        var text = string.Empty;
        for (var i = 0; i < PromptImages.MaxCount; i++) text += attachments.Add(Image(), text, out _);

        Assert.Null(attachments.Add(Image(), text, out var problem));
        Assert.Contains("At most 6", problem);
    }

    [Fact]
    public void Marker_under_the_cursor_is_found_as_a_whole()
    {
        const string text = "x [Image #12] y";
        Assert.Equal((2, 11), ImageAttachments.MarkerEndingAt(text, 13));
        Assert.Equal((2, 11), ImageAttachments.MarkerStartingAt(text, 2));
        Assert.Null(ImageAttachments.MarkerEndingAt(text, 12));
    }

    [Fact]
    public void Only_image_path_tokens_become_attachments()
    {
        var tokens = ImageAttachments.FindPathTokens("""fix @src/A.cs like @shots/a.png and @"C:\My Shots\b.JPG" mail@host.png @x.bmp""");

        Assert.Equal(["shots/a.png", @"C:\My Shots\b.JPG", "x.bmp"], tokens.Select(t => t.Path));
        Assert.Equal("@shots/a.png", "fix @src/A.cs like @shots/a.png"[tokens[0].Start..(tokens[0].Start + tokens[0].Length)]);
        Assert.Equal("shots/a.png", ImageAttachments.PathTokenEndingAt("see @shots/a.png", 16)!.Path);
        Assert.Null(ImageAttachments.PathTokenEndingAt("see @shots/a.png", 10));
    }

    [Fact]
    public void Long_paste_becomes_a_marker_that_expands_when_sent()
    {
        var pastes = new PastedTexts();
        var lines = string.Join("\n", Enumerable.Range(1, 30).Select(i => $"line {i}"));
        var chars = new string('x', 1500);

        Assert.False(PastedTexts.IsLarge(string.Join("\n", Enumerable.Repeat("a", 10))));
        Assert.False(PastedTexts.IsLarge(new string('x', 1000)));
        Assert.True(PastedTexts.IsLarge(string.Join("\n", Enumerable.Repeat("a", 11))));
        Assert.True(PastedTexts.IsLarge(new string('x', 1001)));

        var first = pastes.Add(lines);
        var second = pastes.Add(chars);
        Assert.Equal("[Paste #1, +30 lines]", first);
        Assert.Equal("[Paste #2, 1500 chars]", second);

        // The pasted text comes back verbatim, also when it holds something that looks like a marker.
        var tricky = pastes.Add("see [Paste #9, 3 chars] and [Image #1]\n" + chars);
        Assert.Equal($"fix {lines} and {chars}", pastes.Expand($"fix {first} and {second}"));
        Assert.Equal("see [Paste #9, 3 chars] and [Image #1]\n" + chars, pastes.Expand(tricky));
        Assert.Equal("[Paste #7, 5 chars]", pastes.Expand("[Paste #7, 5 chars]"));

        // Deleted as a whole from either side.
        var text = $"a {first} b";
        Assert.Equal((2, first.Length), PastedTexts.MarkerEndingAt(text, 2 + first.Length));
        Assert.Equal((2, first.Length), PastedTexts.MarkerStartingAt(text, 2));
        Assert.Null(PastedTexts.MarkerEndingAt(text, 1));
    }

    [Fact]
    public void Pasted_or_dropped_image_paths_are_found_only_when_that_is_all_the_text()
    {
        using var workspace = new TempWorkspace();
        var shots = Path.Combine(workspace.Root, "My Shots");
        Directory.CreateDirectory(shots);
        var spaced = Path.Combine(shots, "a.png");
        File.WriteAllBytes(spaced, Png);
        File.WriteAllBytes(Path.Combine(workspace.Root, "b.jpg"), [0xFF, 0xD8, 0xFF, 0xE0]);

        Assert.Equal(["b.jpg"], ImageAttachments.PastedImagePaths(" b.jpg ", workspace.Root));
        // Explorer / Windows Terminal quote a path with spaces, PowerShell in VS Code drops it as & 'path'.
        Assert.Equal([spaced, "b.jpg"], ImageAttachments.PastedImagePaths($"\"{spaced}\" b.jpg", workspace.Root));
        Assert.Equal([spaced], ImageAttachments.PastedImagePaths($"& '{spaced}'", workspace.Root));

        Assert.Null(ImageAttachments.PastedImagePaths("look at b.jpg", workspace.Root));
        Assert.Null(ImageAttachments.PastedImagePaths("gone.png", workspace.Root));
        Assert.Null(ImageAttachments.PastedImagePaths("b.jpg notes.txt", workspace.Root));
        Assert.Null(ImageAttachments.PastedImagePaths("   ", workspace.Root));
    }

    [Fact]
    public void Loading_refuses_missing_large_and_unsupported_files()
    {
        using var language = Lang.Use("en");
        using var workspace = new TempWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Root, "ok.png"), Png);
        File.WriteAllBytes(Path.Combine(workspace.Root, "pic.bmp"), Bmp);
        var large = new byte[8 * 1024 * 1024];
        Png.CopyTo(large, 0);
        File.WriteAllBytes(Path.Combine(workspace.Root, "big.png"), large);

        Assert.Equal("image/png", ImageAttachments.Load("ok.png", workspace.Root, 1, out _)!.MediaType);
        Assert.Null(ImageAttachments.Load("gone.png", workspace.Root, 1, out var missing));
        Assert.Contains("not found", missing);
        Assert.Null(ImageAttachments.Load("big.png", workspace.Root, 1, out var tooLarge));
        Assert.Contains("too large", tooLarge);
        Assert.Null(ImageAttachments.Load("pic.bmp", workspace.Root, 2, out var bmp));
        Assert.Contains("not a png", bmp);
    }

    [Fact]
    public void Text_added_by_a_commit_is_the_tail_of_the_answer()
    {
        Assert.Equal("📤 Committed abc", ThreadFollower.Added("Done.", "Done.\n\n📤 Committed abc"));
        Assert.Equal("all", ThreadFollower.Added(null, " all "));
        Assert.Null(ThreadFollower.Added("x", null));
    }

    [Fact]
    public void Slash_opens_the_command_list_filtered_by_the_word()
    {
        using var language = Lang.Use("en");
        Assert.Equal(SlashCommands.All.Select(c => c.Name), SlashCommands.Matching("/").Select(c => c.Name));
        Assert.Equal(["/project"], SlashCommands.Matching("/PR").Select(c => c.Name));
        Assert.Equal(["/exit"], SlashCommands.Matching("/qu").Select(c => c.Name));
        // Names starting with the word come before names containing it.
        Assert.Equal(["/retry", "/resume", "/project", "/verbose", "/clear"], SlashCommands.Matching("/r").Select(c => c.Name));
        Assert.Empty(SlashCommands.Matching("/project foo"));
        Assert.Empty(SlashCommands.Matching("/src/app.cs"));
        Assert.Empty(SlashCommands.Matching("hello /help"));
        Assert.Empty(SlashCommands.Matching("/help\nmore"));
    }

    [Fact]
    public void Permission_card_offers_one_key_per_choice_without_the_chat_reply()
    {
        using var language = Lang.Use("en");
        var approval = new PendingApprovalDto(
            [new ApprovalItemDto(ApprovalKind.Command, "git grep -n x", null, "git grep *")],
            "The planner wants to run a command.", AllowAlways: true, Text: "🔐 **AIT asks for permission**\n\n1. ...\nReply with the option number.",
            AfterThread: false, AllowPattern: true, AllowAll: true);

        var options = ChatRenderer.ApprovalOptions(approval);
        Assert.Equal("yapsn", string.Concat(options.Select(o => o.Key)));
        // The numbers of the chat card: 1 once, 2 always, 3 deny, 4 pattern, 5 every command.
        Assert.Equal("12453", string.Concat(options.Select(o => o.Number)));
        Assert.Equal([ApprovalDecision.Once, ApprovalDecision.Always, ApprovalDecision.AlwaysPattern, ApprovalDecision.AlwaysAll, ApprovalDecision.Deny],
            options.Select(o => o.Decision));
        Assert.Contains("`git grep *`", options.Single(o => o.Key == 'p').Text);
        Assert.DoesNotContain(options, o => o.Text.Contains("option number"));

        var once = ChatRenderer.ApprovalOptions(approval with { AllowAlways = false, AllowPattern = false, AllowAll = false });
        Assert.Equal("yn", string.Concat(once.Select(o => o.Key)));
    }

    [Fact]
    public void Inline_markdown_becomes_markup_or_plain_text()
    {
        const string reason = "Run `git grep [x]` because **it reads** only";
        Assert.Equal("Run [cyan]git grep [[x]][/] because [bold]it reads[/] only", ChatRenderer.Inline(reason, markup: true));
        Assert.Equal("Run git grep [x] because it reads only", ChatRenderer.Inline(reason, markup: false));
        Assert.Equal("a [[b]] [italic]c[/] 2 * 3", ChatRenderer.Inline("a [b] *c* 2 * 3", markup: true));
        Assert.Equal("[link=https://x.io]docs[/] · docs (https://x.io)", ChatRenderer.Inline("[docs](https://x.io)", markup: true) + " · " + ChatRenderer.Inline("[docs](https://x.io)", markup: false));
    }

    [Fact]
    public void Command_list_completes_and_describes_every_command()
    {
        using var language = Lang.Use("en");
        var project = SlashCommands.All.Single(c => c.Name == "/project");
        Assert.Equal("/project ", SlashCommands.Complete(project));
        Assert.Equal("/help", SlashCommands.Complete(SlashCommands.All[0]));

        var list = SlashCommands.List();
        Assert.All(SlashCommands.All, c => Assert.Contains(c.Usage, list));
        Assert.Contains("quit (also /quit)", list);
    }
}

[SupportedOSPlatform("windows")]
public class ChatWindowsTests
{
    [Fact]
    public void Prompt_mode_turns_image_paths_into_numbered_markers()
    {
        using var workspace = new TempWorkspace();
        var first = Path.Combine(workspace.Root, "first.png");
        var second = Path.Combine(workspace.Root, "my shot.jpg");
        File.WriteAllBytes(first, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]);
        File.WriteAllBytes(second, [0xFF, 0xD8, 0xFF, 0xE0]);
        var attachments = new ImageAttachments();

        var text = ChatCommand.AttachPaths($"""compare @{first} with @"{second}" in @src/A.cs""", attachments, out var problem);

        Assert.Null(problem);
        Assert.Equal("compare [Image #1] with [Image #2] in @src/A.cs", text);
        var (_, images) = attachments.Build(text!);
        Assert.Equal(["image/png", "image/jpeg"], images.Select(i => i.MediaType));
    }

    [Fact]
    public void Prompt_mode_stops_at_a_refused_image()
    {
        using var language = Lang.Use("en");
        Assert.Null(ChatCommand.AttachPaths("see @missing-file.png", new ImageAttachments(), out var problem));
        Assert.Contains("not found", problem);
    }

    [Fact]
    public void Clipboard_bitmap_is_encoded_as_png()
    {
        // 2x2 bottom-up 24 bit DIB: rows padded to 8 bytes.
        var dib = new byte[40 + 16];
        BitConverter.GetBytes(40).CopyTo(dib, 0);
        BitConverter.GetBytes(2).CopyTo(dib, 4);
        BitConverter.GetBytes(2).CopyTo(dib, 8);
        BitConverter.GetBytes((short)1).CopyTo(dib, 12);
        BitConverter.GetBytes((short)24).CopyTo(dib, 14);
        dib[40] = 255; // bottom-left pixel blue

        var png = ClipboardImage.DibToPng(dib)!;

        Assert.Equal("image/png", PromptImages.DetectMediaType(png));
        Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Null(ClipboardImage.DibToPng(new byte[10]));
    }
}
