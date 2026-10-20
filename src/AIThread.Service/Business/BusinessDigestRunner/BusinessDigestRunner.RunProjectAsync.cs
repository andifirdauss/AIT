using AIThread.Common.DTOs;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessDigestRunner
{
    public async Task<BusinessDigestResultDto> RunProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        string key, name;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
                          ?? throw new AppValidationException("Project tidak ditemukan.");
            (key, name) = (project.ProjectKey, project.Name);
        }
        if (string.IsNullOrEmpty(key)) throw new AppValidationException("Project belum punya key; buka lagi menu Project sebentar lagi.");

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
            throw new AppValidationException("Pembaruan business memory sedang berjalan. Tunggu sampai selesai.");
        try
        {
            if (await collector.CountPendingAsync(key, cancellationToken) == 0)
                return new BusinessDigestResultDto(0, 0, 0, 0, ["Tidak ada thread baru yang belum dirangkum untuk project ini."]);
            return await RunKeyAsync(key, name, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
