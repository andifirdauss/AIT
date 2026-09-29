using System.Text;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Telegram;

namespace AiThread.Tests;

public class TelegramMessagesTests
{
    [Theory]
    [InlineData("/project@MyAiThreadBot  2", "project", "2")]
    [InlineData("/STATUS", "status", "")]
    [InlineData("/cancel 1a2b3c4d", "cancel", "1a2b3c4d")]
    [InlineData("/project Nama Project", "project", "Nama Project")]
    public void Commands_are_parsed_without_bot_suffix(string text, string command, string argument)
    {
        Assert.Equal((command, argument), TelegramMessages.ParseCommand(text));
    }

    [Fact]
    public void Button_data_round_trips_and_fits_telegram_limit()
    {
        var id = Guid.NewGuid();
        var data = TelegramMessages.ButtonData(TelegramMessages.ProjectButton, id);

        Assert.True(Encoding.UTF8.GetByteCount(data) <= 64);
        Assert.True(TelegramMessages.TryParseButton(data, TelegramMessages.ProjectButton, out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(TelegramMessages.TryParseButton(data, TelegramMessages.CancelButton, out _));
        Assert.False(TelegramMessages.TryParseButton("p:not-a-guid", TelegramMessages.ProjectButton, out _));
    }

    [Theory]
    [InlineData(42_000, "42s")]
    [InlineData(125_000, "2m 05s")]
    [InlineData(3_725_000, "1j 02m")]
    public void Durations_are_compact(long milliseconds, string expected)
    {
        Assert.Equal(expected, TelegramMessages.Duration(milliseconds));
    }

    [Fact]
    public void Long_paths_keep_their_end()
    {
        var path = TelegramMessages.ShortPath(@"src\AiThread.UI\wwwroot\js\pages\very-long-folder-name\project-detail.js", 40);

        Assert.True(path.Length <= 40);
        Assert.StartsWith("…/", path);
        Assert.EndsWith("project-detail.js", path);
    }

    [Fact]
    public void Progress_lists_tasks_and_offers_planner_activity_only_while_planning()
    {
        var detail = Detail(ThreadStatus.Executing, [TaskDto("t1", ThreadTaskStatus.Succeeded), TaskDto("t2", ThreadTaskStatus.Running)]);

        var text = TelegramMessages.ThreadProgress(detail, "MyApp", "read_file · `a.cs`", DateTime.UtcNow);

        Assert.Contains("⚙️ <b>Eksekusi</b> · 1/2 tugas", text);
        Assert.Contains("✅ <b>t1</b>", text);
        Assert.Contains("🔄 <b>t2</b>", text);
        Assert.DoesNotContain("read_file", text);
    }

    [Fact]
    public void Progress_of_a_huge_plan_stays_within_one_message()
    {
        var tasks = Enumerable.Range(1, 120).Select(i => TaskDto($"t{i}", ThreadTaskStatus.Pending, new string('x', 200))).ToList();

        var text = TelegramMessages.ThreadProgress(Detail(ThreadStatus.Executing, tasks), "MyApp", null, DateTime.UtcNow);

        Assert.True(text.Length < 4096);
        Assert.Contains("tugas lain", text);
    }

    [Fact]
    public void Token_format_matches_botfather_tokens_only()
    {
        Assert.True(TelegramApi.IsValidTokenFormat("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"));
        Assert.False(TelegramApi.IsValidTokenFormat("123456789"));
        Assert.False(TelegramApi.IsValidTokenFormat("123:short"));
        Assert.False(TelegramApi.IsValidTokenFormat("abc:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw"));
    }

    private static ThreadTaskDto TaskDto(string key, ThreadTaskStatus status, string title = "Tambah field") => new(
        Guid.NewGuid(), key, 1, title, $"src/App/{key}.cs", "edit", "", null, status,
        null, null, 0, null, null, null, 0, null, null, null);

    private static ThreadDetailDto Detail(ThreadStatus status, IReadOnlyList<ThreadTaskDto> tasks) => new(
        new ThreadSummaryDto(
            Guid.NewGuid(), Guid.NewGuid(), "MyApp", ThreadSource.Telegram, "telegram:1", "Tambah kolom", status, "Rencana singkat",
            tasks.Count, tasks.Count(t => t.Status == ThreadTaskStatus.Succeeded), tasks.Count(t => t.Status == ThreadTaskStatus.Failed),
            null, null, null, 0, DateTime.UtcNow.AddSeconds(-30), null),
        null, null, null, null, null, 0, 0, 0, 0, null, null, null, tasks);
}
