using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/router")]
public class RouterController(INineRouterService router, INineRouterLauncher launcher, ICliModelService cliModels) : ControllerBase
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

    // Models and combos available through 9router, plus the models of the official CLIs installed here
    // ("cli:claude:opus", ...), for the Node and Setup pickers. Without 9router the CLI models are still listed.
    [HttpGet("models")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RouterModelDto>>>> Models([FromQuery] bool refresh, CancellationToken cancellationToken)
    {
        var cli = await cliModels.GetModelsAsync(cancellationToken);
        IReadOnlyList<RouterModelDto> routed;
        try
        {
            routed = await router.GetModelsAsync(refresh, cancellationToken);
        }
        catch (NineRouterException) when (cli.Count > 0)
        {
            routed = [];
        }
        return ApiResponse<IReadOnlyList<RouterModelDto>>.Ok([.. routed, .. cli]);
    }

    // The official CLIs that can serve as a model engine on this machine (installed, version).
    [HttpGet("cli")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CliBackendStatusDto>>>> Cli([FromQuery] bool refresh, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<CliBackendStatusDto>>.Ok(await cliModels.DetectAsync(refresh, cancellationToken));
}
