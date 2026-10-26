using AIThread.Common.DTOs;
using AIThread.Common.Localization;
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
                          ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
            (key, name) = (project.ProjectKey, project.Name);
        }
        if (string.IsNullOrEmpty(key)) throw new AppValidationException(Lang.T("Project belum punya key; buka lagi menu Project sebentar lagi.", "The project has no key yet; open the Project menu again in a moment."));

        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken))
            throw new AppValidationException(Lang.T("Pembaruan business memory sedang berjalan. Tunggu sampai selesai.", "A business memory update is running. Wait until it finishes."));
        try
        {
            if (await collector.CountPendingAsync(key, cancellationToken) == 0)
                return new BusinessDigestResultDto(0, 0, 0, 0, [Lang.T("Tidak ada thread baru yang belum dirangkum untuk project ini.", "There are no new threads to summarize for this project.")]);
            return await RunKeyAsync(key, name, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
