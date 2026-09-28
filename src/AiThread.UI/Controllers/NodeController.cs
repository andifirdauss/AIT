using Microsoft.AspNetCore.Mvc;

namespace AiThread.UI.Controllers;

public class NodeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
