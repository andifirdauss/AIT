using AIThread.Common.Localization;
using AIThread.Service.Memory;

namespace AIThread.Tests;

public class PromptLanguageTests
{
    [Theory]
    [InlineData("tolong tambahkan kolom deskripsi di form user", TextLanguage.Indonesian)]
    [InlineData("kenapa endpoint login error 500?", TextLanguage.Indonesian)]
    [InlineData("ulangi", TextLanguage.Indonesian)]
    [InlineData("fix the null reference in UserService and add a test", TextLanguage.English)]
    [InlineData("why does the login endpoint return 500?", TextLanguage.English)]
    [InlineData("retry", TextLanguage.English)]
    public void The_words_of_the_prompt_decide(string prompt, string expected) => Assert.Equal(expected, TextLanguage.OfPrompt(prompt));

    [Fact]
    public void English_technical_words_do_not_outweigh_an_Indonesian_request() =>
        Assert.Equal(TextLanguage.Indonesian, TextLanguage.OfPrompt("tolong refactor UserController, tambahkan validation di field email pakai regex yang sama dengan form register"));

    [Fact]
    public void Pasted_code_and_reminders_do_not_count()
    {
        const string prompt = """
            perbaiki ini dong
            ```csharp
            // the user should not be able to add an order when the account is locked
            public void Add(Order order) { if (order is null) throw new ArgumentNullException(nameof(order)); }
            ```
            <system-reminder>The user is in the project folder. Please check the files you need and do not guess.</system-reminder>
            """;
        Assert.Equal(TextLanguage.Indonesian, TextLanguage.OfPrompt(prompt));
    }

    [Fact]
    public void Without_words_the_earlier_user_turns_decide()
    {
        const string conversation = "[user] tolong buatkan halaman laporan penjualan\n\n[assistant] The report page was added to the project.\n\n[user] sudah, sekarang tambahkan filter tanggal";
        Assert.Equal(TextLanguage.Indonesian, TextLanguage.OfPrompt("👍", conversation));
        Assert.Null(TextLanguage.OfPrompt("👍"));
    }

    [Fact]
    public void An_Indonesian_prompt_answers_in_Indonesian_while_the_UI_is_English()
    {
        using var _ = Lang.Use("en");
        Assert.Equal(Lang.Indonesian, TextLanguage.LangOfPrompt("tolong cek kenapa build gagal"));
        Assert.Equal(Lang.English, TextLanguage.LangOfPrompt("👍"));
        using (Lang.Use("id")) Assert.Equal(Lang.English, TextLanguage.LangOfPrompt("please check why the build fails"));
    }
}
