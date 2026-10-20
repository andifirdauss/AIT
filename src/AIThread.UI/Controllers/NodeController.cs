using Microsoft.AspNetCore.Mvc;

namespace AIThread.UI.Controllers;

public class NodeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
