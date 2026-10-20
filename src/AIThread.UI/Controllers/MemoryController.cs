using Microsoft.AspNetCore.Mvc;

namespace AIThread.UI.Controllers;

public class MemoryController : Controller
{
    public IActionResult Index(Guid? projectId)
    {
        ViewData["ProjectId"] = projectId;
        return View();
    }
}
