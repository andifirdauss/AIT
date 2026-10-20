using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/router")]
public class RouterController(INineRouterService router, INineRouterLauncher launcher) : ControllerBase
{
    // Dashboard button: start the local 9router, installing it with npm when it is missing.
    [HttpPost("start")]
    public async Task<ActionResult<ApiResponse<RouterLaunchResultDto>>> Start(CancellationToken cancellationToken)
    {
        var result = await launcher.EnsureRunningAsync(manual: true, cancellationToken);
        return ApiResponse<RouterLaunchResultDto>.Ok(result, result.Message);
    }

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
