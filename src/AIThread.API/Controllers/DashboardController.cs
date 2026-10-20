using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(IDashboardService dashboardService, IActivityLogService activityLog) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> Summary(CancellationToken cancellationToken) =>
        ApiResponse<DashboardSummaryDto>.Ok(await dashboardService.GetSummaryAsync(cancellationToken));

    [HttpGet("logs")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ActivityLogDto>>>> Logs(
        [FromQuery] int take = 200, [FromQuery] ActivityLevel? minLevel = null, [FromQuery] Guid? threadId = null, CancellationToken cancellationToken = default) =>
        ApiResponse<IReadOnlyList<ActivityLogDto>>.Ok(await activityLog.GetRecentAsync(take, minLevel, threadId, cancellationToken));
}
