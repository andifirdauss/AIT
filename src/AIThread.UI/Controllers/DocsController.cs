using AIThread.Service.Interfaces;
using AIThread.UI.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.UI.Controllers;

// The user guide from the repository docs/ folder: /Docs is the index, /Docs/{slug} one page.
[Route("Docs")]
public class DocsController(IDocsService docs) : Controller
{
    [HttpGet("")]
    [HttpGet("{slug}")]
    public IActionResult Index(string? slug)
    {
        var page = docs.Get(slug);
        if (page is null && docs.DirectoryPath is not null) Response.StatusCode = StatusCodes.Status404NotFound;

        ViewData["Title"] = page?.Entry.Title ?? "Docs";
        return View(new DocsViewModel(docs.List(), page, docs.DirectoryPath is not null));
    }
}
