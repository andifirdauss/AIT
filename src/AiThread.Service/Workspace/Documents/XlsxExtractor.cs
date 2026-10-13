using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace AiThread.Service.Workspace.Documents;

public sealed class XlsxExtractor : IDocumentExtractor
{
    private const int MaxRowsPerSheet = 5000;

    public IReadOnlyCollection<string> Extensions { get; } = [".xlsx"];

    public Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken)
    {
        using var doc = SpreadsheetDocument.Open(fullPath, false);
        var wb = doc.WorkbookPart;
        if (wb is null)
        {
            return Task.FromResult("");
        }

        var sharedStrings = wb.SharedStringTablePart?.SharedStringTable?
            .Elements<SharedStringItem>()
            .Select(i => i.InnerText)
            .ToArray() ?? [];

        var sb = new StringBuilder();

        foreach (var sheet in wb.Workbook?.Sheets?.Elements<Sheet>() ?? Enumerable.Empty<Sheet>())
        {
            var part = (WorksheetPart)wb.GetPartById(sheet.Id!);
            sb.Append("--- Sheet: ").Append(sheet.Name).Append(" ---\n");

            int rowsWritten = 0;
            foreach (var row in part.Worksheet?.Descendants<Row>() ?? Enumerable.Empty<Row>())
            {
                var values = new List<string>();
                int next = 0;

                foreach (var cell in row.Elements<Cell>())
                {
                    int idx = cell.CellReference?.Value is { } reference
                        ? ColumnIndex(reference)
                        : next;

                    while (values.Count <= idx)
                    {
                        values.Add("");
                    }

                    values[idx] = CellText(cell, sharedStrings);
                    next = idx + 1;
                }

                if (values.All(string.IsNullOrEmpty))
                {
                    continue;
                }

                if (rowsWritten == MaxRowsPerSheet)
                {
                    sb.Append("... (sheet truncated at 5000 rows)\n");
                    break;
                }

                sb.Append(string.Join('\t', values).TrimEnd('\t')).Append('\n');
                rowsWritten++;
            }

            sb.Append('\n');
        }

        return Task.FromResult(sb.ToString().TrimEnd() + "\n");
    }

    private static int ColumnIndex(string cellReference)
    {
        int col = 0;
        foreach (char c in cellReference)
        {
            if (!char.IsLetter(c))
            {
                break;
            }

            col = col * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return col - 1;
    }

    private static string CellText(Cell cell, string[] sharedStrings)
    {
        string text;
        var dataType = cell.DataType?.Value;

        if (dataType == CellValues.SharedString)
        {
            text = int.TryParse(cell.CellValue?.Text, out var index)
                && index >= 0 && index < sharedStrings.Length
                ? sharedStrings[index]
                : "";
        }
        else if (dataType == CellValues.InlineString)
        {
            text = cell.InlineString?.InnerText ?? "";
        }
        else if (dataType == CellValues.Boolean)
        {
            text = cell.CellValue?.Text == "1" ? "TRUE" : "FALSE";
        }
        else
        {
            text = cell.CellValue?.Text ?? "";
        }

        return text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
