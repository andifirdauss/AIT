using AIThread.Common.DTOs;
using AIThread.Service.Business;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Business memory: the business wiki of each project (Memory menu → tab Business memory) and its settings.
[ApiController]
[Route("api/business")]
public class BusinessController(IBusinessMemoryService business, IBusinessDigestRunner runner) : ControllerBase
{
    // Base64 of the largest accepted image, with room for a data: URL prefix.
    private const int MaxUploadChars = (BusinessImages.MaxBytes + 2) / 3 * 4 + 100;

    [HttpGet("projects/{projectId:guid}")]
    public async Task<ActionResult<ApiResponse<BusinessOverviewDto>>> Overview(Guid projectId, CancellationToken cancellationToken) =>
        await business.GetOverviewAsync(projectId, cancellationToken) is { } overview
            ? ApiResponse<BusinessOverviewDto>.Ok(overview)
            : NotFound(ApiResponse<BusinessOverviewDto>.Fail("Project tidak ditemukan."));

    [HttpGet("pages/{id:guid}")]
    public async Task<ActionResult<ApiResponse<BusinessPageDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await business.GetPageAsync(id, cancellationToken) is { } page
            ? ApiResponse<BusinessPageDto>.Ok(page)
            : NotFound(ApiResponse<BusinessPageDto>.Fail("Halaman tidak ditemukan."));

    [HttpPost("pages")]
    public async Task<ActionResult<ApiResponse<BusinessPageDto>>> Create(SaveBusinessPageRequest request, CancellationToken cancellationToken) =>
        ApiResponse<BusinessPageDto>.Ok(await business.CreatePageAsync(request, cancellationToken), "Halaman dibuat.");

    // 409 when someone else saved the page since request.Version.
    [HttpPut("pages/{id:guid}")]
    public async Task<ActionResult<ApiResponse<BusinessPageDto>>> Update(Guid id, SaveBusinessPageRequest request, CancellationToken cancellationToken) =>
        await business.UpdatePageAsync(id, request, cancellationToken) is { } page
            ? ApiResponse<BusinessPageDto>.Ok(page, "Halaman disimpan.")
            : NotFound(ApiResponse<BusinessPageDto>.Fail("Halaman tidak ditemukan."));

    [HttpDelete("pages/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken) =>
        await business.DeletePageAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Halaman dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Halaman tidak ditemukan."));

    [HttpGet("pages/{id:guid}/revisions")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<BusinessRevisionDto>>>> Revisions(Guid id, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<BusinessRevisionDto>>.Ok(await business.GetRevisionsAsync(id, cancellationToken));

    [HttpGet("pages/{id:guid}/revisions/{version:int}")]
    public async Task<ActionResult<ApiResponse<BusinessRevisionDto>>> Revision(Guid id, int version, CancellationToken cancellationToken) =>
        await business.GetRevisionAsync(id, version, cancellationToken) is { } revision
            ? ApiResponse<BusinessRevisionDto>.Ok(revision)
            : NotFound(ApiResponse<BusinessRevisionDto>.Fail("Revisi tidak ditemukan."));

    [HttpPost("pages/{id:guid}/revisions/{version:int}/restore")]
    public async Task<ActionResult<ApiResponse<BusinessPageDto>>> Restore(Guid id, int version, CancellationToken cancellationToken) =>
        await business.RestoreAsync(id, version, cancellationToken) is { } page
            ? ApiResponse<BusinessPageDto>.Ok(page, $"Halaman dipulihkan dari versi {version}.")
            : NotFound(ApiResponse<BusinessPageDto>.Fail("Halaman atau revisi tidak ditemukan."));

    // Renders the editor's Markdown exactly as a saved page is shown.
    [HttpPost("preview")]
    public ActionResult<ApiResponse<string>> Preview(PreviewBusinessPageRequest request) =>
        ApiResponse<string>.Ok(business.Render(request.Content));

    [HttpPost("assets")]
    public async Task<ActionResult<ApiResponse<BusinessAssetDto>>> Upload(UploadBusinessAssetRequest request, CancellationToken cancellationToken)
    {
        var data = request.Data ?? string.Empty;
        if (data.Length > MaxUploadChars) throw new AppValidationException($"Gambar maksimal {BusinessImages.MaxBytes / 1024 / 1024} MB.");
        var comma = data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? data.IndexOf(',') : -1;

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(comma >= 0 ? data[(comma + 1)..] : data);
        }
        catch (FormatException)
        {
            throw new AppValidationException("Data gambar tidak valid.");
        }
        return ApiResponse<BusinessAssetDto>.Ok(await business.AddAssetAsync(request.ProjectId, request.PageId, bytes, request.Caption, cancellationToken));
    }

    // Images never change once stored (a new image gets a new id), so browsers may cache them for good.
    [HttpGet("assets/{id:guid}")]
    public async Task<IActionResult> Asset(Guid id, CancellationToken cancellationToken)
    {
        if (await business.GetAssetAsync(id, cancellationToken) is not { } asset) return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(asset.Data, asset.MediaType);
    }

    // "Perbarui sekarang": summarizes the project's waiting threads into the wiki right away.
    [HttpPost("digest")]
    public async Task<ActionResult<ApiResponse<BusinessDigestResultDto>>> Digest([FromQuery] Guid projectId, CancellationToken cancellationToken)
    {
        var result = await runner.RunProjectAsync(projectId, cancellationToken);
        var message = result.Threads == 0 && result.Warnings.Count > 0
            ? result.Warnings[0]
            : $"{result.Threads} thread dirangkum: {result.PagesCreated} halaman baru, {result.PagesUpdated} diperbarui.";
        return ApiResponse<BusinessDigestResultDto>.Ok(result, message);
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ApiResponse<BusinessSettingsDto>>> Settings(CancellationToken cancellationToken) =>
        ApiResponse<BusinessSettingsDto>.Ok(await business.GetSettingsAsync(cancellationToken));

    [HttpPut("developer")]
    public async Task<ActionResult<ApiResponse<BusinessSettingsDto>>> SetDeveloper(SetDeveloperNameRequest request, CancellationToken cancellationToken) =>
        ApiResponse<BusinessSettingsDto>.Ok(await business.SetDeveloperNameAsync(request.Name, cancellationToken), "Nama developer disimpan.");

    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<BusinessSettingsDto>>> UpdateSettings(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken) =>
        ApiResponse<BusinessSettingsDto>.Ok(await business.UpdateSettingsAsync(request, cancellationToken), "Setting business memory disimpan.");
}
