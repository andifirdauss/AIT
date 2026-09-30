using System.Diagnostics;
using System.Net;
using AiThread.Common.DTOs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace AiThread.API.Controllers;

// Control endpoints for a background instance (scripts/aithread.ps1, AiThread.exe stop/menu).
[ApiController]
[Route("api/system")]
public class SystemController(IHostApplicationLifetime lifetime, IHostEnvironment environment) : ControllerBase
{
    // A custom header forces a CORS preflight, so a web page open in the browser cannot trigger a shutdown.
    public const string ShutdownHeader = "X-AiThread-Shutdown";

    private static readonly DateTime StartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    [HttpGet]
    public ActionResult<ApiResponse<SystemInfoDto>> Get() =>
        ApiResponse<SystemInfoDto>.Ok(new SystemInfoDto(Environment.ProcessId, StartedAt, environment.EnvironmentName, environment.ContentRootPath));

    // Graceful stop: running threads are cancelled and recorded before the process exits.
    [HttpPost("shutdown")]
    public ActionResult<ApiResponse<bool>> Shutdown()
    {
        var remote = HttpContext.Connection.RemoteIpAddress;
        if (remote is { IsIPv4MappedToIPv6: true }) remote = remote.MapToIPv4();
        if (remote is null || !IPAddress.IsLoopback(remote) || !Request.Headers.ContainsKey(ShutdownHeader))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<bool>.Fail("Shutdown hanya boleh dari komputer ini lewat scripts/aithread.ps1 atau AiThread.exe."));

        // Stop after the response is sent; stopping right away would abort this request.
        Response.OnCompleted(() =>
        {
            lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return ApiResponse<bool>.Ok(true, "AiThread dimatikan.");
    }
}
