using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AIThread.Service.Workspace.Documents;

public sealed class PdfExtractor : IDocumentExtractor
{
    private static readonly string[] SupportedExtensions = [".pdf"];

    public IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();

        using var document = PdfDocument.Open(fullPath);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            builder.AppendLine($"--- Page {page.Number} ---");

            var pageText = ContentOrderTextExtractor.GetText(page).Trim();
            if (pageText.Length == 0)
            {
                builder.AppendLine("(no text on this page; it may be a scanned image)");
            }
            else
            {
                builder.AppendLine(pageText);
            }

            builder.AppendLine();
        }

        var text = builder.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        return Task.FromResult(text.TrimEnd() + "\n");
    }
}
