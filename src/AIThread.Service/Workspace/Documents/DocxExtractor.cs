using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AIThread.Service.Workspace.Documents;

public sealed class DocxExtractor : IDocumentExtractor
{
    public IReadOnlyCollection<string> Extensions { get; } = [".docx"];

    public Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken)
    {
        using var doc = WordprocessingDocument.Open(fullPath, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Task.FromResult("");
        }

        var text = new StringBuilder();
        var emptyRun = 0;

        void AppendLine(string line)
        {
            if (line.Length == 0)
            {
                emptyRun++;
                if (emptyRun > 1)
                {
                    return;
                }
            }
            else
            {
                emptyRun = 0;
            }

            text.Append(line).Append('\n');
        }

        foreach (var element in body.ChildElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (element is Paragraph paragraph)
            {
                var line = paragraph.InnerText;
                var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
                if (styleId is not null
                    && styleId.StartsWith("Heading", StringComparison.Ordinal)
                    && styleId.Length == "Heading".Length + 1
                    && char.IsDigit(styleId[^1]))
                {
                    var level = styleId[^1] - '0';
                    line = new string('#', level) + " " + line;
                }

                AppendLine(line);
            }
            else if (element is Table table)
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    var cells = row.Elements<TableCell>()
                        .Select(cell => cell.InnerText.Trim());
                    AppendLine(string.Join(" | ", cells));
                }

                AppendLine("");
            }
        }

        return Task.FromResult(text.ToString().TrimEnd() + "\n");
    }
}
