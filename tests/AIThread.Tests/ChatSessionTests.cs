using System.Reflection;
using AIThread.API.Controllers;
using AIThread.Common.Chat;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.UI.Hosting.Chat;

namespace AIThread.Tests;

// aithread chat sessions: the key, finding a session to resume, the earlier turns the planner gets, the session list.
public class ChatSessionTests
{
    private static ChatSessionDto Session(string hex, int turns = 1, int hoursAgo = 1) =>
        new($"{ChatSessionKey.Prefix}{hex}", "first", "last", turns, DateTime.UtcNow.AddHours(-hoursAgo - 1), DateTime.UtcNow.AddHours(-hoursAgo), ThreadStatus.Completed);

    [Fact]
    public void Session_key_is_chat_and_32_hex_digits()
    {
        var key = ChatSessionKey.New();
        Assert.True(ChatSessionKey.IsValid(key));
        Assert.Equal(key[5..13], ChatSessionKey.Short(key));
        Assert.False(ChatSessionKey.IsValid(null));
        Assert.False(ChatSessionKey.IsValid("chat:XYZ"));
        Assert.False(ChatSessionKey.IsValid(PendingQuestionStore.ThreadSession(Guid.NewGuid())));
        Assert.False(ChatSessionKey.IsValid("claude-session-1"));
    }

    [Fact]
    public void Session_to_resume_is_found_by_its_id_prefix_or_whole_key()
    {
        using var language = Lang.Use("en");
        var sessions = new[] { Session("a1b2c3d4" + new string('0', 24)), Session("a1b2ffff" + new string('0', 24)), Session("0badcafe" + new string('1', 24)) };

        Assert.Same(sessions[2], ChatSessions.Find(sessions, "0BAD").Session);
        Assert.Same(sessions[0], ChatSessions.Find(sessions, "a1b2c3").Session);
        Assert.Same(sessions[1], ChatSessions.Find(sessions, sessions[1].SessionId).Session);
        Assert.Contains("matches 2 sessions", ChatSessions.Find(sessions, "a1b2").Error);
        Assert.Contains("was not found", ChatSessions.Find(sessions, "beef").Error);
        Assert.NotNull(ChatSessions.Find(sessions, "  ").Error);
    }

    [Fact]
    public void Session_is_described_with_its_turns_and_age()
    {
        using var language = Lang.Use("en");
        var now = DateTime.UtcNow;
        Assert.Equal("a1b2c3d4 · first · 3 prompts · 2 h ago", ChatSessions.Describe(Session("a1b2c3d4" + new string('0', 24), 3, 2) with { LastAt = now.AddHours(-2) }, now));
        Assert.Equal("just now", ChatSessions.Ago(now, now));
        Assert.Equal("3 d ago", ChatSessions.Ago(now.AddDays(-3), now));
    }

    [Fact]
    public async Task Chat_session_gives_the_planner_its_latest_pack_and_the_turns_after_it()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var session = ChatSessionKey.New();
        var start = DateTime.UtcNow.AddMinutes(-30);
        var first = await AddThreadAsync(harness, session, "first change", start, pack: "PACK after the first change");
        var second = await AddThreadAsync(harness, session, "second change", start.AddMinutes(5));
        var third = await AddThreadAsync(harness, session, "third change", start.AddMinutes(10));
        await AddThreadAsync(harness, ChatSessionKey.New(), "another session", start.AddMinutes(12));

        var chat = await harness.Service<IContextKeeper>().ResolveChatAsync(harness.ProjectId, session, CancellationToken.None);

        Assert.NotNull(chat);
        Assert.Equal([first, second, third], chat.Context.ThreadIds);
        Assert.Equal(first, chat.Context.PackThreadId);
        Assert.Equal("PACK after the first change", chat.Context.Pack);
        Assert.Contains("second change", chat.Conversation);
        Assert.Contains("third change", chat.Conversation);
        Assert.DoesNotContain("first change", chat.Conversation);
        Assert.DoesNotContain("another session", chat.Conversation);

        Assert.Null(await harness.Service<IContextKeeper>().ResolveChatAsync(harness.ProjectId, ChatSessionKey.New(), CancellationToken.None));
    }

    [Fact]
    public async Task Chat_sessions_are_listed_most_recent_first_and_their_threads_found()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var older = ChatSessionKey.New();
        var newer = ChatSessionKey.New();
        var start = DateTime.UtcNow.AddHours(-3);
        await AddThreadAsync(harness, older, "Rubah registry ke AIT\nsupaya rapi", start);
        await AddThreadAsync(harness, older, "lanjut ke csproj", start.AddMinutes(10));
        var latest = await AddThreadAsync(harness, newer, "jelaskan PastedTexts", start.AddHours(1));
        await AddThreadAsync(harness, "claude-cli-session", "from a CLI", start.AddHours(2), ThreadSource.ClaudeCode);
        await AddThreadAsync(harness, null, "from the dashboard", start.AddHours(2));

        var threads = harness.Service<IThreadService>();
        var sessions = await threads.GetChatSessionsAsync(harness.ProjectId, 10);

        Assert.Equal([newer, older], sessions.Select(s => s.SessionId));
        Assert.Equal(2, sessions[1].Turns);
        Assert.Equal("Rubah registry ke AIT", sessions[1].Title);
        Assert.Equal("lanjut ke csproj", sessions[1].LastPrompt);
        Assert.Equal([latest], (await threads.GetRecentAsync(10, harness.ProjectId, sessionId: newer)).Select(t => t.Id));
    }

    [Fact]
    public async Task Start_refuses_a_foreign_session_key_and_gives_a_chat_its_earlier_turns()
    {
        using var language = Lang.Use("en");
        var orchestrator = DispatchProxy.Create<IThreadOrchestrator, ChatTests.RecordingOrchestrator>();
        var recorder = (ChatTests.RecordingOrchestrator)(object)orchestrator;
        var keeper = new FakeContextKeeper();
        var controller = new ThreadsController(null!, orchestrator, null!, null!, null!, null!, keeper);
        var projectId = Guid.NewGuid();

        var refused = await Assert.ThrowsAsync<AppValidationException>(() =>
            controller.Start(new StartThreadRequest(projectId, "hi", null, "thread:abc"), CancellationToken.None));
        Assert.Contains("chat:<32 hex digits>", refused.Message);

        var session = ChatSessionKey.New();
        await controller.Start(new StartThreadRequest(projectId, "go on", null, session), CancellationToken.None);
        var request = recorder.Request!;
        Assert.Equal(session, request.SessionId);
        Assert.Equal((projectId, session), keeper.Asked);
        Assert.Equal("PACK", request.Session!.Pack);
        Assert.Equal("### thread 1", request.ConversationContext);

        await controller.Start(new StartThreadRequest(projectId, "on its own"), CancellationToken.None);
        Assert.Null(recorder.Request!.SessionId);
        Assert.Null(recorder.Request.Session);
    }

    private static async Task<Guid> AddThreadAsync(OrchestrationHarness harness, string? session, string prompt, DateTime createdAt,
        ThreadSource source = ThreadSource.Dashboard, string? pack = null)
    {
        await using var db = await harness.CreateDbAsync();
        var thread = new ThreadRun
        {
            ProjectId = harness.ProjectId,
            Source = source,
            SessionId = session,
            Prompt = prompt,
            Status = ThreadStatus.Completed,
            PlanSummary = prompt,
            FinalResponse = "Done: " + prompt,
            ContextPack = pack,
            CreatedAt = createdAt,
            CompletedAt = createdAt.AddMinutes(1)
        };
        db.Threads.Add(thread);
        await db.SaveChangesAsync();
        return thread.Id;
    }

    private sealed class FakeContextKeeper : IContextKeeper
    {
        public (Guid ProjectId, string SessionId)? Asked { get; private set; }

        public Task<ResolvedChat?> ResolveChatAsync(Guid projectId, string sessionId, CancellationToken cancellationToken)
        {
            Asked = (projectId, sessionId);
            return Task.FromResult<ResolvedChat?>(new ResolvedChat(new SessionContext(Guid.NewGuid(), "PACK", []), "### thread 1"));
        }

        public Task<ResolvedSession?> ResolveAsync(Guid projectId, IReadOnlyList<AIThread.Service.Proxy.ThreadAnchorRef> anchors, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> ReadThreadAsync(Guid projectId, string id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(ThreadRun thread, ThreadRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(string Pack, string Author)> WritePackAsync(string input, Func<string> fallback, Guid? threadId, Guid? projectId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
