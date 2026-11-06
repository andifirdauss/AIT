using System.IO.Compression;
using System.Text.Json;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore
{
    private TableSnapshot Deserialize(byte[] bytes, out string checksum)
    {
        using var buffer = new MemoryStream(_protector.Unprotect(bytes));
        using var gzip = new GZipStream(buffer, CompressionMode.Decompress);
        var file = JsonSerializer.Deserialize<BackupFile>(gzip, Json) ?? throw new DatabaseToolException("the backup file is empty");
        var width = file.Columns.Count;
        var rows = file.Rows.Select(r => Enumerable.Range(0, width).Select(i => new Cell(r[width + i] ?? "s", r[i])).ToArray()).ToList();
        checksum = file.Checksum;
        return new TableSnapshot { Table = new TableName(file.Schema, file.Table), Columns = file.Columns, Rows = rows, TakenAt = file.TakenAt };
    }
}
