using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AiThread.Service.Implementations;

public sealed partial class DocsService
{
    private static string PlainText(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var builder = new StringBuilder();
        foreach (var inline in container.Descendants<Inline>())
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
            }
        }
        return builder.ToString().Trim();
    }
}
