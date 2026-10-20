using Microsoft.AspNetCore.Mvc;

namespace AIThread.UI.Controllers;

public class ThreadController : Controller
{
    public IActionResult Detail(Guid id)
    {
        ViewData["ThreadId"] = id;
        return View();
    }
}
