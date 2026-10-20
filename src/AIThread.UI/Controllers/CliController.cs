using Microsoft.AspNetCore.Mvc;

namespace AIThread.UI.Controllers;

public class CliController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
