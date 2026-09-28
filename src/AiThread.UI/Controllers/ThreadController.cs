using Microsoft.AspNetCore.Mvc;

namespace AiThread.UI.Controllers;

public class ThreadController : Controller
{
    public IActionResult Detail(Guid id)
    {
        ViewData["ThreadId"] = id;
        return View();
    }
}
