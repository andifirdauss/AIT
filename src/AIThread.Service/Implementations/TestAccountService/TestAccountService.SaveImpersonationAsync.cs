using AIThread.Service.Interfaces;
using AIThread.Common.DTOs;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task<TestingSettingsDto> SaveImpersonationAsync(Guid projectId, SaveImpersonationRequest request, CancellationToken cancellationToken = default)
    {
        var (project, by) = await ContextAsync(projectId, cancellationToken);
        var status = request.Status?.Trim().ToLowerInvariant();
        if (status is not (ImpersonationInfo.Available or ImpersonationInfo.None or ImpersonationInfo.Unknown))
            throw new AppValidationException("Status login as harus available, none, atau unknown.");
        var how = string.IsNullOrWhiteSpace(request.How) ? null : request.How.Trim();
        if (status == ImpersonationInfo.Available && how is null)
            throw new AppValidationException("Tulis cara memakai login as (menu, URL, atau endpoint) bila statusnya tersedia.");
        var roles = (request.Roles ?? []).Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (status == ImpersonationInfo.Available && roles.Count == 0)
            throw new AppValidationException("Isi peran akun uji yang boleh memakai login as (mis. Administrator).");

        var previous = (await store.GetAsync(project.Key, cancellationToken)).Impersonation;
        var info = new ImpersonationInfo(status, how, string.IsNullOrWhiteSpace(request.Back) ? null : request.Back.Trim(), roles,
            previous?.Evidence ?? [], ImpersonationInfo.FromDeveloper, request.Confirmed, DateTime.UtcNow, by);
        var settings = await store.SaveImpersonationAsync(project.Key, info, by, cancellationToken);
        activityLog.Info(LogCategory, $"Login as {project.Name}: {status}{(request.Confirmed ? " (dikonfirmasi)" : string.Empty)}.", projectId: projectId);
        return TestAccountText.ToDto(settings);
    }
}
