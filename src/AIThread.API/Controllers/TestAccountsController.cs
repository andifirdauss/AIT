using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// The test accounts of a project, its development password and its "login as" detection (Project > Detail → Akun uji).
[ApiController]
[Route("api/projects/{projectId:guid}")]
public class TestAccountsController(ITestAccountService accounts) : ControllerBase
{
    [HttpGet("test-accounts")]
    public async Task<ActionResult<ApiResponse<TestAccountsDto>>> Get(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<TestAccountsDto>.Ok(await accounts.GetAsync(projectId, cancellationToken));

    [HttpPost("test-accounts")]
    public async Task<ActionResult<ApiResponse<TestAccountDto>>> Add(Guid projectId, SaveTestAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.AddAsync(projectId, request, cancellationToken);
        return ApiResponse<TestAccountDto>.Ok(account, $"Akun uji {account.Username} ditambahkan.");
    }

    [HttpPut("test-accounts/{accountId:guid}")]
    public async Task<ActionResult<ApiResponse<TestAccountDto>>> Update(Guid projectId, Guid accountId, SaveTestAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.UpdateAsync(projectId, accountId, request, cancellationToken);
        return ApiResponse<TestAccountDto>.Ok(account, $"Akun uji {account.Username} disimpan.");
    }

    [HttpDelete("test-accounts/{accountId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid projectId, Guid accountId, CancellationToken cancellationToken)
    {
        await accounts.DeleteAsync(projectId, accountId, cancellationToken);
        return ApiResponse<bool>.Ok(true, "Akun uji dihapus.");
    }

    // DevPassword empty: back to the default.
    [HttpPut("testing-settings")]
    public async Task<ActionResult<ApiResponse<TestingSettingsDto>>> SaveSettings(Guid projectId, SaveTestingSettingsRequest request, CancellationToken cancellationToken) =>
        ApiResponse<TestingSettingsDto>.Ok(await accounts.SaveSettingsAsync(projectId, request, cancellationToken), "Password dev disimpan.");

    // A scan of the code and a cheap model; the result stays unconfirmed until a developer checks it.
    [HttpPost("testing-settings/impersonation/detect")]
    public async Task<ActionResult<ApiResponse<TestingSettingsDto>>> DetectImpersonation(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<TestingSettingsDto>.Ok(await accounts.DetectImpersonationAsync(projectId, cancellationToken), "Deteksi login as selesai.");

    [HttpPut("testing-settings/impersonation")]
    public async Task<ActionResult<ApiResponse<TestingSettingsDto>>> SaveImpersonation(Guid projectId, SaveImpersonationRequest request, CancellationToken cancellationToken) =>
        ApiResponse<TestingSettingsDto>.Ok(await accounts.SaveImpersonationAsync(projectId, request, cancellationToken), "Login as disimpan.");
}
