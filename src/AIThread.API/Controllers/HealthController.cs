using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController(IHealthService healthService) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<string>> Get() => ApiResponse<string>.Ok(healthService.GetStatus());
}
