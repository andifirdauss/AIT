using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Skills;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    // The calls the model gave for one function that are really written there, resolved to their declarations.
    public static List<CodeFunctionCall> Calls(JsonObject answer, FunctionSpan span, string path, string[] lines, FileOutline outline, StructureSymbolIndex symbols)
    {
        var calls = new List<CodeFunctionCall>();
        var seen = new HashSet<(CodeCallKind, string)>();
        var body = string.Join('\n', lines.Skip(span.Line - 1).Take(span.EndLine - span.Line + 1));
        foreach (var item in (answer["calls"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var target = SkillAi.Text(item["target"])?.Trim();
            if (string.IsNullOrEmpty(target) || target.Length > MaxTargetLength) continue;
            var kind = KindOf(SkillAi.Text(item["kind"]));
            var line = LineOf(item["line"]);
            if (line < span.Line || line > span.EndLine || !Written(kind, target, line, lines, body)) continue;
            if (!seen.Add((kind, target))) continue;

            var resolved = CallResolver.Resolve(kind, target, path, outline, symbols);
            calls.Add(new CodeFunctionCall
            {
                Kind = kind,
                Target = target,
                Line = line,
                ResolvedPath = resolved?.Path,
                ResolvedSymbol = resolved?.Symbol,
                ResolvedLine = resolved?.Line
            });
        }
        return calls;
    }
}
