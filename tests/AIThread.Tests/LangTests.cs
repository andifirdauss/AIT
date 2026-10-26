using AIThread.Common.Localization;

namespace AIThread.Tests;

public class LangTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("EN-us", "en")]
    [InlineData("English", "en")]
    [InlineData("id", "id")]
    [InlineData(null, "id")]
    [InlineData("fr", "id")]
    public void Language_codes_are_normalized(string? code, string expected) => Assert.Equal(expected, Lang.Normalize(code));

    [Fact]
    public void A_scope_picks_the_text_and_restores_the_language()
    {
        var before = Lang.Current;
        using (Lang.Use("en"))
        {
            Assert.Equal("Saved.", Lang.T("Tersimpan.", "Saved."));
            Assert.Equal("en-US", Lang.Culture.Name);
            using (Lang.Use("id")) Assert.Equal("Tersimpan.", Lang.T("Tersimpan.", "Saved."));
            Assert.True(Lang.IsEnglish);
        }
        Assert.Equal(before, Lang.Current);
    }
}
