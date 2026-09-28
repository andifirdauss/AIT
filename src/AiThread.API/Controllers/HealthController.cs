using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(IHealthService healthService) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<string>> Get() => ApiResponse<string>.Ok(healthService.GetStatus());
}
