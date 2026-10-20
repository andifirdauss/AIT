using System.Text.Json.Nodes;
using AIThread.Service.Implementations;
using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public class PlannerQuestionsTests
{
    private static readonly PlannerQuestion Question = new("Kolom deskripsi wajib diisi?",
    [
        new QuestionOption("Opsional", "NULL di database, tanpa validasi"),
        new QuestionOption("Wajib", null)
    ]);

    [Fact]
    public void ToText_numbers_the_options_and_says_how_to_answer()
    {
        var text = Question.ToText();

        Assert.Contains("Kolom deskripsi wajib diisi?", text);
        Assert.Contains("1. **Opsional** — NULL di database, tanpa validasi", text);
        Assert.Contains("2. **Wajib**\n", text);
        Assert.Contains("nomor pilihan", text);
    }

    [Theory]
    [InlineData("2")]
    [InlineData(" 2. ")]
    [InlineData("2)")]
    public void AnswerFor_spells_out_a_chosen_option_number(string reply)
    {
        Assert.Equal("The developer chose option 2: Wajib.", Question.AnswerFor(reply));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("opsional saja, maks 500 karakter")]
    public void AnswerFor_passes_other_replies_through(string reply)
    {
        var answer = Question.AnswerFor(reply);

        Assert.StartsWith("The developer replied", answer);
        Assert.EndsWith(reply, answer);
    }

    [Fact]
    public void Store_hands_the_question_to_the_next_prompt_of_the_same_session_once()
    {
        var store = new PendingQuestionStore(new ManualTime());
        var projectId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        store.Add(threadId, projectId, "session-1", Question, Conversation());

        Assert.Null(store.TryTake("other-session", projectId, out _));
        Assert.Equal(threadId, store.TryTake("session-1", projectId, out var stale)?.ThreadId);
        Assert.Null(stale);
        Assert.Null(store.TryTake("session-1", projectId, out _));
    }

    [Fact]
    public void Store_drops_a_question_of_another_project_or_past_its_lifetime_as_stale()
    {
        var time = new ManualTime();
        var store = new PendingQuestionStore(time);
        var projectId = Guid.NewGuid();
        var otherProject = store.Add(Guid.NewGuid(), projectId, "a", Question, Conversation());
        var expired = store.Add(Guid.NewGuid(), projectId, "b", Question, Conversation());

        Assert.Null(store.TryTake("a", Guid.NewGuid(), out var stale));
        Assert.Equal(otherProject.ThreadId, stale?.ThreadId);

        time.Advance(PendingQuestionStore.Lifetime + TimeSpan.FromMinutes(1));
        Assert.Null(store.TryTake("b", projectId, out stale));
        Assert.Equal(expired.ThreadId, stale?.ThreadId);
    }

    [Fact]
    public void Store_takes_expired_questions_and_removes_by_thread()
    {
        var time = new ManualTime();
        var store = new PendingQuestionStore(time);
        var projectId = Guid.NewGuid();
        var old = store.Add(Guid.NewGuid(), projectId, "old", Question, Conversation());
        time.Advance(PendingQuestionStore.Lifetime);
        var fresh = store.Add(Guid.NewGuid(), projectId, "fresh", Question, Conversation());

        Assert.Equal([old.ThreadId], store.TakeExpired().Select(q => q.ThreadId));
        Assert.True(store.Remove(fresh.ThreadId));
        Assert.False(store.Remove(fresh.ThreadId));
    }

    [Fact]
    public void Ask_user_is_offered_before_submit_plan_only_when_asking_is_possible()
    {
        var names = (bool canAsk) => PlannerTools.All(canBuild: true, canAsk).Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();

        Assert.DoesNotContain(PlannerTools.AskUser, names(false));
        var withAsk = names(true);
        Assert.Equal(PlannerTools.SubmitPlan, withAsk[^1]);
        Assert.Equal(PlannerTools.AskUser, withAsk[^2]);
    }

    [Theory]
    [InlineData("""{"prompt_tokens":1000,"prompt_tokens_details":{"cached_tokens":800}}""", 800)]
    [InlineData("""{"prompt_tokens":1000,"cache_read_input_tokens":640}""", 640)]
    [InlineData("""{"prompt_tokens":1000}""", null)]
    public void Cached_tokens_are_read_from_openai_and_translated_claude_usage(string usage, int? expected)
    {
        Assert.Equal(expected, NineRouterService.CachedTokensOf(JsonNode.Parse(usage)));
    }

    private static PlannerConversation Conversation() => new([], Guid.NewGuid(), 3, 100, 0, "call_1", 1, false);

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
