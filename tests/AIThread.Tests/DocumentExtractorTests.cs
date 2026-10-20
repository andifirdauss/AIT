using System.IO.Compression;
using AIThread.Service.Workspace;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AIThread.Tests;

public class DocumentExtractorTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Docx_paragraphs_headings_and_tables_are_extracted()
    {
        CreateDocx("order.docx");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("order.docx", null, null);

        Assert.Contains("(extracted text, lines", result);
        Assert.Contains("# Order rules", result);
        Assert.Contains("Total must be positive", result);
        Assert.Contains("Qty | Price", result);
    }

    [Fact]
    public async Task Xlsx_sheets_are_extracted_as_tab_separated_rows()
    {
        var path = Path.Combine(_workspace.Root, "orders.xlsx");
        using (var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = doc.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(
                    new S.Cell
                    {
                        CellReference = "A1",
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(new S.Text("Code")),
                    },
                    new S.Cell { CellReference = "C1", CellValue = new S.CellValue("42") })
                { RowIndex = 1 },
                new S.Row(
                    new S.Cell
                    {
                        CellReference = "A2",
                        DataType = S.CellValues.SharedString,
                        CellValue = new S.CellValue("0"),
                    })
                { RowIndex = 2 }));

            var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedPart.SharedStringTable = new S.SharedStringTable(
                new S.SharedStringItem(new S.Text("Shared")));

            var relId = workbookPart.GetIdOfPart(worksheetPart);
            workbookPart.Workbook = new S.Workbook(new S.Sheets(
                new S.Sheet { Name = "Orders", SheetId = 1, Id = relId }));
        }
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("orders.xlsx", null, null);

        Assert.Contains("--- Sheet: Orders ---", result);
        Assert.Contains("Code\t\t42", result);
        Assert.Contains("Shared", result);
    }

    [Fact]
    public async Task Pptx_slide_text_and_speaker_notes_are_extracted()
    {
        var path = Path.Combine(_workspace.Root, "flow.pptx");
        using (var doc = PresentationDocument.Create(path, PresentationDocumentType.Presentation))
        {
            var presPart = doc.AddPresentationPart();
            var slidePart = presPart.AddNewPart<SlidePart>();
            slidePart.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(
                TitleShape("Checkout flow"))));

            var notes = slidePart.AddNewPart<NotesSlidePart>();
            notes.NotesSlide = new P.NotesSlide(new P.CommonSlideData(new P.ShapeTree(
                TitleShape("Explain the retry rule"))));

            presPart.Presentation = new P.Presentation(new P.SlideIdList(
                new P.SlideId { Id = 256, RelationshipId = presPart.GetIdOfPart(slidePart) }));
        }
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("flow.pptx", null, null);

        Assert.Contains("--- Slide 1 ---", result);
        Assert.Contains("Checkout flow", result);
        Assert.Contains("Notes:", result);
        Assert.Contains("Explain the retry rule", result);
    }

    [Fact]
    public async Task Pdf_text_is_extracted_per_page()
    {
        var path = Path.Combine(_workspace.Root, "invoice.pdf");
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Invoice approval", 12, new PdfPoint(50, 700), font);
        File.WriteAllBytes(path, builder.Build());
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("invoice.pdf", null, null);

        Assert.Contains("--- Page 1 ---", result);
        Assert.Contains("Invoice approval", result);
    }

    [Fact]
    public async Task Ipynb_cells_and_text_outputs_are_extracted()
    {
        _workspace.Write("nb.ipynb", """
            {
              "cells": [
                { "cell_type": "markdown", "metadata": {}, "source": ["# Title\n", "intro"] },
                {
                  "cell_type": "code",
                  "metadata": {},
                  "execution_count": 1,
                  "source": "print(1)",
                  "outputs": [
                    { "output_type": "stream", "name": "stdout", "text": ["1\n"] },
                    { "output_type": "display_data", "data": { "image/png": "AAAA" }, "metadata": {} }
                  ]
                }
              ],
              "metadata": {},
              "nbformat": 4,
              "nbformat_minor": 5
            }
            """);
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("nb.ipynb", null, null);

        Assert.Contains("--- Cell 1 [markdown] ---", result);
        Assert.Contains("intro", result);
        Assert.Contains("--- Cell 2 [code] ---", result);
        Assert.Contains("print(1)", result);
        Assert.Contains("Output:", result);
        Assert.Contains("[image output omitted]", result);
    }

    [Fact]
    public async Task Odt_and_ods_content_is_extracted()
    {
        const string ns =
            "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
            "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
            "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\"";

        var odtBody =
            "<office:body><office:text>" +
            "<text:h text:outline-level=\"2\">Scope</text:h>" +
            "<text:p>Hello<text:s/>world</text:p>" +
            "</office:text></office:body>";
        WriteZip("doc.odt", $"<office:document-content {ns}>{odtBody}</office:document-content>");

        var odsBody =
            "<office:body><office:spreadsheet>" +
            "<table:table table:name=\"Data\"><table:table-row>" +
            "<table:table-cell><text:p>A</text:p></table:table-cell>" +
            "<table:table-cell table:number-columns-repeated=\"2\"/>" +
            "<table:table-cell><text:p>B</text:p></table:table-cell>" +
            "</table:table-row></table:table>" +
            "</office:spreadsheet></office:body>";
        WriteZip("book.ods", $"<office:document-content {ns}>{odsBody}</office:document-content>");

        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var odt = await tools.ReadFileAsync("doc.odt", null, null);
        Assert.Contains("## Scope", odt);
        Assert.Contains("Hello world", odt);

        var ods = await tools.ReadFileAsync("book.ods", null, null);
        Assert.Contains("--- Sheet: Data ---", ods);
        Assert.Contains("A\t\t\tB", ods);
    }

    [Fact]
    public async Task Legacy_office_formats_ask_for_conversion()
    {
        _workspace.Write("old.doc", "x");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("old.doc", null, null);

        Assert.StartsWith("Error: '.doc' is a legacy binary Office format", result);
        Assert.Contains(".docx", result);
    }

    [Fact]
    public async Task Corrupt_document_reports_an_extraction_error()
    {
        _workspace.Write("broken.docx", "not a zip");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("broken.docx", null, null);

        Assert.StartsWith("Error: could not extract text from 'broken.docx'", result);
    }

    [Fact]
    public async Task Extracted_text_supports_line_ranges()
    {
        CreateDocx("ranges.docx");
        var tools = new WorkspaceTools(new WorkspaceScope(_workspace.Root));

        var result = await tools.ReadFileAsync("ranges.docx", 2, 2);

        Assert.Contains("(extracted text, lines 2-2 of", result);
    }

    private string CreateDocx(string name)
    {
        var path = Path.Combine(_workspace.Root, name);
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new W.Document(new W.Body(
            new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
                new W.Run(new W.Text("Order rules"))),
            new W.Paragraph(new W.Run(new W.Text("Total must be positive"))),
            new W.Table(new W.TableRow(
                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Qty")))),
                new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Price"))))))));
        return path;
    }

    private void WriteZip(string name, string contentXml)
    {
        var path = Path.Combine(_workspace.Root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("content.xml");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(contentXml);
    }

    private static P.Shape TitleShape(string text) => new(
        new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = 2, Name = "Title" },
            new P.NonVisualShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.ShapeProperties(),
        new P.TextBody(
            new A.BodyProperties(),
            new A.Paragraph(new A.Run(new A.Text(text)))));
}
