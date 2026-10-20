using System.Net;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    public static string HtmlToText(string html)
    {
        var text = ScriptOrStyle().Replace(html, " ");
        text = BlockTag().Replace(text, "\n");
        text = AnyTag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Spaces().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n").Trim();
        return text.Length <= MaxPageChars ? text : text[..MaxPageChars] + "\n…";
    }
}
