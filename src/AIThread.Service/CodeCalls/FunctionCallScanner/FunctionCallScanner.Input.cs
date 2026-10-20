using System.Text;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private static string Input(string path, string language, string[] lines, List<Pending> batch)
    {
        var builder = new StringBuilder($"# File\n{path} ({language})\n");
        for (var i = 0; i < batch.Count; i++)
        {
            var span = batch[i].Span;
            builder.Append($"\n## F{i + 1} {span.Name} (lines {span.Line}-{span.EndLine})\n");
            var written = 0;
            for (var n = span.Line; n <= span.EndLine && n <= lines.Length; n++)
            {
                var text = $"{n}| {lines[n - 1]}\n";
                if (written + text.Length > MaxBodyChars)
                {
                    builder.Append($"… (lines {n}-{span.EndLine} left out: function too long)\n");
                    break;
                }
                builder.Append(text);
                written += text.Length;
            }
        }
        return builder.ToString();
    }
}
