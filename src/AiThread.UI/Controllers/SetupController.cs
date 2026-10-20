using Microsoft.AspNetCore.Mvc;

namespace AiThread.UI.Controllers;

public class SetupController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
