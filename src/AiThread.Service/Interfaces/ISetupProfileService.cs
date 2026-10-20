using AiThread.Common.DTOs;

namespace AiThread.Service.Interfaces;

// Team profile (menu Setup): what a team shares (9router address, orchestration settings, nodes, memory stores,
// business memory and skills) written to a file without secrets, and applied on another machine.
public interface ISetupProfileService
{
    Task<SetupProfileFile> ExportAsync(CancellationToken cancellationToken = default);

    // Idempotent: importing the same file again only applies what differs. A section that fails is reported in Errors
    // and the other sections are still applied.
    Task<SetupProfileImportResultDto> ImportAsync(SetupProfileImportRequest request, CancellationToken cancellationToken = default);
}
