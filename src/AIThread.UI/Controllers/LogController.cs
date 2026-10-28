using Microsoft.AspNetCore.Mvc;

namespace AIThread.UI.Controllers;

// Log & diagnostics: incidents, activity log, run logs, and the diagnostic ZIP (data from /api/diagnostics).
public class LogController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
