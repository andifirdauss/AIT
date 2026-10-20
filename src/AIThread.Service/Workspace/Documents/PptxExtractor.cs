using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace AIThread.Service.Workspace.Documents;

public sealed class PptxExtractor : IDocumentExtractor
{
    private static readonly string[] SupportedExtensions = [".pptx"];

    public IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public Task<string> ExtractAsync(string fullPath, CancellationToken cancellationToken)
    {
        using var doc = PresentationDocument.Open(fullPath, false);
        var presPart = doc.PresentationPart;
        if (presPart is null)
        {
            return Task.FromResult("");
        }

        var text = new StringBuilder();
        var slideIds = presPart.Presentation?.SlideIdList?.Elements<SlideId>();
        var n = 1;

        if (slideIds is not null)
        {
            foreach (var slideId in slideIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var slidePart = (SlidePart)presPart.GetPartById(slideId.RelationshipId!);
                text.Append("--- Slide ").Append(n).Append(" ---\n");

                foreach (var p in slidePart.Slide.Descendants<A.Paragraph>())
                {
                    var line = string.Concat(p.Descendants<A.Text>().Select(t => t.Text)).Trim();
                    if (line.Length > 0)
                    {
                        text.Append(line).Append('\n');
                    }
                }

                var noteLines = new List<string>();
                if (slidePart.NotesSlidePart?.NotesSlide is { } notesSlide)
                {
                    foreach (var shape in notesSlide.Descendants<Shape>())
                    {
                        var placeholderType = shape.NonVisualShapeProperties?
                            .ApplicationNonVisualDrawingProperties?
                            .PlaceholderShape?.Type?.Value;

                        if (placeholderType == PlaceholderValues.SlideImage
                            || placeholderType == PlaceholderValues.SlideNumber)
                        {
                            continue;
                        }

                        foreach (var p in shape.Descendants<A.Paragraph>())
                        {
                            var line = string.Concat(p.Descendants<A.Text>().Select(t => t.Text)).Trim();
                            if (line.Length > 0)
                            {
                                noteLines.Add(line);
                            }
                        }
                    }
                }

                if (noteLines.Count > 0)
                {
                    text.Append("Notes:\n");
                    foreach (var line in noteLines)
                    {
                        text.Append(line).Append('\n');
                    }
                }

                text.Append('\n');
                n++;
            }
        }

        return Task.FromResult(text.ToString().TrimEnd() + "\n");
    }
}
