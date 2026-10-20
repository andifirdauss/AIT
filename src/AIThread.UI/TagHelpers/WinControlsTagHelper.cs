using System.Net;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AIThread.UI.TagHelpers;

// The project's window controls, macOS style: red = close, yellow = minimize, green = maximize. Behavior lives in
// site.js (data-win) and Bootstrap (data-bs-dismiss); wwwroot/js/site.js winControls() renders the same markup for
// windows built in JavaScript.
//   <win-controls />                                   a .win panel (close only works when the page handles win:close)
//   <win-controls for="modal" />                       a Bootstrap modal header
//   <win-controls for="alert" />                       a dismissible alert: the close button alone
//   <win-controls close-label="Kosongkan log" />       what the red button does, as its tooltip
//   <win-controls disabled="close" />                  buttons shown grey and inactive
[HtmlTargetElement("win-controls", TagStructure = TagStructure.WithoutEndTag)]
public sealed class WinControlsTagHelper : TagHelper
{
    // window | modal | alert | toast
    public string For { get; set; } = "window";

    public string? CloseLabel { get; set; }

    // Comma-separated: close, min, max.
    public string? Disabled { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var disabled = (Disabled ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var closeOnly = For is "alert" or "toast";
        var dismiss = For is "modal" or "alert" or "toast" ? $" data-bs-dismiss=\"{For}\"" : " data-win=\"close\"";

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "win-controls");
        output.Attributes.SetAttribute("role", "group");
        output.Attributes.SetAttribute("aria-label", "Kontrol jendela");

        var html = Button("close", CloseLabel ?? "Tutup", dismiss, disabled);
        if (!closeOnly)
        {
            var minLabel = For == "modal" ? "Kecilkan" : "Lipat";
            html += Button("min", minLabel, " data-win=\"min\"", disabled) + Button("max", "Layar penuh", " data-win=\"max\"", disabled);
        }
        output.Content.SetHtmlContent(html);
    }

    private static string Button(string kind, string label, string action, string[] disabled)
    {
        var off = disabled.Contains(kind, StringComparer.OrdinalIgnoreCase);
        var text = WebUtility.HtmlEncode(label);
        return $"<button type=\"button\" class=\"win-btn win-{kind}\"{(off ? " disabled" : action)} aria-label=\"{text}\" title=\"{text}\"></button>";
    }
}
