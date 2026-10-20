using System.Diagnostics.CodeAnalysis;

namespace AIThread.Service.Workspace.Documents;

// New formats are added by implementing IDocumentExtractor and listing it here.
public static class DocumentExtractors
{
    public const long MaxDocumentBytes = 20_000_000;

    private static readonly Dictionary<string, IDocumentExtractor> ByExtension = BuildByExtension();

    private static readonly Dictionary<string, string> Legacy = new(StringComparer.OrdinalIgnoreCase)
    {
        [".doc"] = ".docx",
        [".xls"] = ".xlsx",
        [".ppt"] = ".pptx",
    };

    public static bool TryGet(string extension, [NotNullWhen(true)] out IDocumentExtractor? extractor)
    {
        if (string.IsNullOrEmpty(extension))
        {
            extractor = null;
            return false;
        }

        return ByExtension.TryGetValue(extension, out extractor);
    }

    public static string? LegacyFormatMessage(string extension)
    {
        if (Legacy.TryGetValue(extension, out var modern))
        {
            return $"'{extension.ToLowerInvariant()}' is a legacy binary Office format. Convert it to {modern} (for example Save As in Office or LibreOffice) and read the converted file.";
        }

        return null;
    }

    private static Dictionary<string, IDocumentExtractor> BuildByExtension()
    {
        var extractors = new IDocumentExtractor[]
        {
            new PdfExtractor(),
            new DocxExtractor(),
            new XlsxExtractor(),
            new PptxExtractor(),
            new IpynbExtractor(),
            new OpenDocumentExtractor(),
        };

        var map = new Dictionary<string, IDocumentExtractor>(StringComparer.OrdinalIgnoreCase);
        foreach (var extractor in extractors)
        {
            foreach (var extension in extractor.Extensions)
            {
                map[extension] = extractor;
            }
        }

        return map;
    }
}
