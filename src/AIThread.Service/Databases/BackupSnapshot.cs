using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Databases;

// A backup file on disk (SnapshotFile format) and what is known of it: shape, scope, row count, checksum, size. Rows
// are never held in memory; ReadRowsAsync streams them and fails at the end when the file does not match Checksum.
// An INSERT's backup holds no rows: the keys it added are written beside it (AddedKeysPath) when it runs.
public sealed class BackupSnapshot(string path, IDataProtector root, SnapshotHeader header, long rows, string checksum, long bytes)
{
    public string Path { get; } = path;
    public SnapshotHeader Header { get; } = header;
    public long Rows { get; } = rows;
    public string Checksum { get; } = checksum;
    public long Bytes { get; } = bytes;

    public string AddedKeysPath => Path + ".added";

    public bool HasAddedKeys => File.Exists(AddedKeysPath);

    public IAsyncEnumerable<Cell[]> ReadRowsAsync(CancellationToken cancellationToken) => SnapshotReader.ReadRowsAsync(Path, root, Checksum, cancellationToken);

    // Each item is the key cells of a row the INSERT added (Header.KeysOnly() order).
    public IAsyncEnumerable<Cell[]> ReadAddedKeysAsync(CancellationToken cancellationToken) => SnapshotReader.ReadRowsAsync(AddedKeysPath, root, null, cancellationToken);

    // Written to a temporary file and moved into place only once complete, so a half-written list never exists.
    public async Task<long> WriteAddedKeysAsync(IAsyncEnumerable<Cell[]> keys, long reserve, CancellationToken cancellationToken)
    {
        var temporary = AddedKeysPath + ".tmp";
        try
        {
            SnapshotSummary summary;
            await using (var writer = await SnapshotWriter.CreateAsync(temporary, root, Header.KeysOnly(), reserve, null, cancellationToken))
            {
                await foreach (var key in keys.WithCancellation(cancellationToken)) await writer.WriteAsync(key, cancellationToken);
                summary = await writer.CompleteAsync(cancellationToken);
            }
            File.Move(temporary, AddedKeysPath, overwrite: true);
            return summary.Rows;
        }
        catch (IOException ex)
        {
            TryDelete(temporary);
            throw new DatabaseToolException($"the keys of the new rows could not be written next to the backup: {ex.Message}", ex);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public void DeleteAddedKeys() => TryDelete(AddedKeysPath);

    // Reads the header only; rows are verified when they are read.
    public static async Task<BackupSnapshot> OpenAsync(string path, IDataProtector root, long rows, string checksum, CancellationToken cancellationToken)
    {
        var header = await SnapshotReader.ReadHeaderAsync(path, root, cancellationToken);
        return new BackupSnapshot(path, root, header, rows, checksum, new FileInfo(path).Length);
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the retention cleanup.
        }
    }
}
