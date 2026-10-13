using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace AiThread.Service.Workspace.Documents;

public sealed class OpenDocumentExtractor : IDocumentExtractor
{
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    private const long MaxContentBytes = 50_000_000;
    private const int MaxRepeat = 256;
    private const int MaxRowsPerSheet = 5000;

    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".odt", ".ods" };

    public Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var zip = ZipFile.OpenRead(fullPath);
        var entry = zip.GetEntry("content.xml")
            ?? throw new InvalidDataException("content.xml not found; not an OpenDocument file");
        if (entry.Length > MaxContentBytes)
            throw new InvalidDataException("content.xml is too large");

        XDocument doc;
        using (var stream = entry.Open())
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            doc = XDocument.Load(reader);
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var text = extension == ".ods" ? ExtractSpreadsheet(doc) : ExtractDocument(doc);

        return Task.FromResult(text.TrimEnd() + "\n");
    }

    private static string ExtractDocument(XDocument doc)
    {
        var sb = new StringBuilder();
        var body = doc.Root?.Element(Office + "body")?.Element(Office + "text");
        if (body != null)
            WalkBlocks(body, sb);
        return sb.ToString();
    }

    private static void WalkBlocks(XElement parent, StringBuilder sb)
    {
        foreach (var child in parent.Elements())
        {
            if (child.Name == Text + "h")
            {
                var level = PositiveInt(child.Attribute(Text + "outline-level"), 1);
                sb.Append(new string('#', level)).Append(' ').Append(ParagraphText(child)).Append('\n');
            }
            else if (child.Name == Text + "p")
            {
                sb.Append(ParagraphText(child)).Append('\n');
            }
            else if (child.Name == Table + "table")
            {
                WriteTable(child, sb);
            }
            else
            {
                WalkBlocks(child, sb);
            }
        }
    }

    private static void WriteTable(XElement table, StringBuilder sb)
    {
        foreach (var row in table.Elements().SelectMany(RowsOf))
        {
            var cells = row.Elements()
                .Where(e => e.Name == Table + "table-cell" || e.Name == Table + "covered-table-cell")
                .Select(CellText);
            sb.Append(string.Join(" | ", cells)).Append('\n');
        }
        sb.Append('\n');
    }

    private static string ExtractSpreadsheet(XDocument doc)
    {
        var sb = new StringBuilder();
        var spreadsheet = doc.Root?.Element(Office + "body")?.Element(Office + "spreadsheet");
        if (spreadsheet == null)
            return string.Empty;

        foreach (var sheet in spreadsheet.Elements(Table + "table"))
        {
            sb.Append("--- Sheet: ").Append((string?)sheet.Attribute(Table + "name") ?? string.Empty).Append(" ---\n");

            var emitted = 0;
            var truncated = false;
            foreach (var row in sheet.Elements().SelectMany(RowsOf))
            {
                var line = BuildSpreadsheetRow(row);
                if (line.Length == 0)
                    continue;

                var repeat = Math.Min(PositiveInt(row.Attribute(Table + "number-rows-repeated"), 1), MaxRepeat);
                for (var i = 0; i < repeat; i++)
                {
                    if (emitted >= MaxRowsPerSheet)
                    {
                        truncated = true;
                        break;
                    }
                    sb.Append(line).Append('\n');
                    emitted++;
                }

                if (truncated)
                    break;
            }

            if (truncated)
                sb.Append("... (sheet truncated at 5000 rows)\n");

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static IEnumerable<XElement> RowsOf(XElement container)
    {
        if (container.Name == Table + "table-row")
            return new[] { container };
        if (container.Name == Table + "table-row-group" || container.Name == Table + "table-header-rows" || container.Name == Table + "table-rows")
            return container.Elements().SelectMany(RowsOf);
        return Enumerable.Empty<XElement>();
    }

    private static string BuildSpreadsheetRow(XElement row)
    {
        var sb = new StringBuilder();
        foreach (var cell in row.Elements()
                     .Where(e => e.Name == Table + "table-cell" || e.Name == Table + "covered-table-cell"))
        {
            var repeat = Math.Min(PositiveInt(cell.Attribute(Table + "number-columns-repeated"), 1), MaxRepeat);
            var text = CellText(cell);
            for (var i = 0; i < repeat; i++)
                sb.Append(text).Append('\t');
        }
        return sb.ToString().TrimEnd('\t');
    }

    private static string CellText(XElement cell)
    {
        var parts = cell.Descendants()
            .Where(e => e.Name == Text + "p" || e.Name == Text + "h")
            .Select(ParagraphText)
            .Where(s => s.Length > 0);
        return string.Join(" ", parts);
    }

    private static string ParagraphText(XElement paragraph)
    {
        var sb = new StringBuilder();
        AppendInline(paragraph, sb);
        return sb.ToString().Trim();
    }

    private static void AppendInline(XNode node, StringBuilder sb)
    {
        if (node is XText text)
        {
            sb.Append(text.Value);
            return;
        }

        if (node is not XElement element)
            return;

        if (element.Name == Text + "s")
        {
            sb.Append(' ', PositiveInt(element.Attribute(Text + "c"), 1));
            return;
        }
        if (element.Name == Text + "tab")
        {
            sb.Append('\t');
            return;
        }
        if (element.Name == Text + "line-break")
        {
            sb.Append(' ');
            return;
        }

        foreach (var child in element.Nodes())
            AppendInline(child, sb);
    }

    private static int PositiveInt(XAttribute? attribute, int fallback)
    {
        if (attribute != null && int.TryParse(attribute.Value, out var value) && value > 0)
            return value;
        return fallback;
    }
}
