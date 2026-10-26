using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Memory;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private const int MaxDescribedChars = 60;

    // The progress line of a tester tool: what is opened, clicked, typed or requested, in the developer's terms.
    public static string Describe(string tool, JsonObject args, string baseUrl)
    {
        string Element() => Short(PlannerToolRunner.Str(args, "element") ?? PlannerToolRunner.Str(args, "selector") ?? PlannerToolRunner.Str(args, "ref") ?? "?");
        string Url() => Relative(PlannerToolRunner.Str(args, "url") ?? "/", baseUrl);
        return tool switch
        {
            TesterTools.HttpRequest => $"{(PlannerToolRunner.Str(args, "method") ?? "GET").ToUpperInvariant()} {Url()}",
            TesterTools.BrowserOpen => Url(),
            TesterTools.BrowserClick => Element(),
            TesterTools.BrowserFill => $"{Element()} ← \"{Short(args["text"] is JsonValue v && v.TryGetValue<string>(out var text) ? text : string.Empty)}\""
                                       + (PlannerToolRunner.Bool(args, "submit") ? " ⏎" : string.Empty),
            TesterTools.BrowserSelect => $"{Element()} ← {Short(string.Join(", ", MemoryAi.Strings(args["values"])))}",
            TesterTools.BrowserPress => PlannerToolRunner.Str(args, "key") ?? string.Empty,
            TesterTools.BrowserWait => PlannerToolRunner.Str(args, "text") is { } waitFor
                ? $"\"{Short(waitFor)}\"{(PlannerToolRunner.Bool(args, "gone") ? Lang.T(" hilang", " gone") : string.Empty)}"
                : $"{(args["seconds"] is JsonValue s && s.TryGetValue<double>(out var seconds) ? seconds : 5):0.#}s",
            TesterTools.SwitchUser => $"👤 {PlannerToolRunner.Str(args, "account") ?? "?"}",
            TesterTools.Remember => $"{PlannerToolRunner.Str(args, "name") ?? "?"} = \"{Short(PlannerToolRunner.Str(args, "value") ?? string.Empty)}\"",
            _ => string.Empty
        };
    }

    // "/api/users" for a URL under the base URL; other URLs as they are.
    private static string Relative(string url, string baseUrl)
    {
        var root = baseUrl.TrimEnd('/');
        return url.StartsWith(root, StringComparison.OrdinalIgnoreCase) && (url.Length == root.Length || url[root.Length] is '/' or '?')
            ? url.Length == root.Length ? "/" : url[root.Length..]
            : url;
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        return line.Length > MaxDescribedChars ? line[..MaxDescribedChars] + "…" : line;
    }
}
