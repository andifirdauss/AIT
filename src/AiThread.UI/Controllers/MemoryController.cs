using Microsoft.AspNetCore.Mvc;

namespace AiThread.UI.Controllers;

public class MemoryController : Controller
{
    public IActionResult Index(Guid? projectId)
    {
        ViewData["ProjectId"] = projectId;
        return View();
    }
}
