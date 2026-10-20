using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AIThread.UI.Models;

namespace AIThread.UI.Controllers;

// Dashboard: 9router connection, pipeline readiness, threads and live log.
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
