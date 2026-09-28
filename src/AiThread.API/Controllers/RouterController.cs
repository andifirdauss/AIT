using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/router")]
public class RouterController(INineRouterService router) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<ApiResponse<RouterStatusDto>>> Status([FromQuery] bool refresh, CancellationToken cancellationToken)
    {
        var status = !refresh && router.LastStatus is { } cached ? cached : await router.CheckStatusAsync(cancellationToken);
        return ApiResponse<RouterStatusDto>.Ok(status);
    }

    // Models and combos available through 9router, for the Node and Setting pickers.
    [HttpGet("models")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RouterModelDto>>>> Models([FromQuery] bool refresh, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<RouterModelDto>>.Ok(await router.GetModelsAsync(refresh, cancellationToken));
}
