using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/nodes")]
public class NodesController(INodeService nodeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NodeDto>>>> GetAll(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<NodeDto>>.Ok(await nodeService.GetAllAsync(cancellationToken));

    [HttpGet("pipeline")]
    public async Task<ActionResult<ApiResponse<PipelineStatusDto>>> Pipeline(CancellationToken cancellationToken) =>
        ApiResponse<PipelineStatusDto>.Ok(await nodeService.GetPipelineStatusAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<NodeDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await nodeService.GetAsync(id, cancellationToken) is { } node
            ? ApiResponse<NodeDto>.Ok(node)
            : NotFound(ApiResponse<NodeDto>.Fail(Lang.T("Node tidak ditemukan.", "Node not found.")));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<NodeDto>>> Create(SaveNodeRequest request, CancellationToken cancellationToken) =>
        ApiResponse<NodeDto>.Ok(await nodeService.CreateAsync(request, cancellationToken), Lang.T("Node ditambahkan.", "Node added."));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<NodeDto>>> Update(Guid id, SaveNodeRequest request, CancellationToken cancellationToken) =>
        await nodeService.UpdateAsync(id, request, cancellationToken) is { } node
            ? ApiResponse<NodeDto>.Ok(node, Lang.T("Node diperbarui.", "Node updated."))
            : NotFound(ApiResponse<NodeDto>.Fail(Lang.T("Node tidak ditemukan.", "Node not found.")));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken) =>
        await nodeService.DeleteAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Node dihapus.", "Node deleted."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Node tidak ditemukan.", "Node not found.")));

    [HttpPost("{id:guid}/test")]
    public async Task<ActionResult<ApiResponse<NodeTestResultDto>>> Test(Guid id, CancellationToken cancellationToken) =>
        ApiResponse<NodeTestResultDto>.Ok(await nodeService.TestAsync(id, cancellationToken));
}
