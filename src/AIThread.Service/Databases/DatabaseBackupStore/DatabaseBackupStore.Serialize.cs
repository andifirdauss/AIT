using System.IO.Compression;
using System.Text.Json;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    // JSON → gzip → Data Protection. Each row is its texts followed by its tags (Cell), so types survive the trip.
    private byte[] Serialize(TableSnapshot snapshot)
    {
        var rows = snapshot.Rows.Select(r => r.Select(c => c.Text).Concat(r.Select(c => (string?)c.Tag)).ToArray()).ToList();
        var file = new BackupFile(snapshot.Table.Schema, snapshot.Table.Name, snapshot.Columns.ToList(), rows, snapshot.TakenAt, snapshot.Checksum());
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(gzip, file, Json);
        return _protector.Protect(buffer.ToArray());
    }
}
