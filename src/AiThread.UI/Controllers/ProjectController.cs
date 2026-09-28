using Microsoft.AspNetCore.Mvc;

namespace AiThread.UI.Controllers;

public class ProjectController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Detail(Guid id)
    {
        ViewData["ProjectId"] = id;
        return View();
    }
}
