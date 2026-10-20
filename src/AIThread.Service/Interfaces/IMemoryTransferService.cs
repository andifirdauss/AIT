using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// Export and import of short and long memory as a portable file, whatever store each level uses.
public interface IMemoryTransferService
{
    // The memories matching the Memory menu filters (paging ignored). Throws when short memory is wanted but its
    // store is not connected, rather than leaving it out silently.
    Task<MemoryExportFile> ExportAsync(MemoryQuery filter, CancellationToken cancellationToken = default);

    // Adds the memories of the file this store does not have yet, by id and by content. Idempotent: importing the
    // same file again changes nothing. A dry run reports what would happen without writing.
    Task<MemoryImportResultDto> ImportAsync(MemoryExportFile file, MemoryImportMode mode, bool dryRun, CancellationToken cancellationToken = default);
}
