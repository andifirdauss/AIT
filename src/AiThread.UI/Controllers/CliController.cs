using Microsoft.AspNetCore.Mvc;

namespace AiThread.UI.Controllers;

public class CliController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
