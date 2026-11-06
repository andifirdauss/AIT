using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Databases;

// The backup file (format 2), written and read as a stream so a table of any size passes through a bounded buffer:
//   "AITDBK2\n" · file id (16 bytes) · frames
// A frame is [int32 length][Data Protection payload]; the payload is [int32 sequence][byte kind][body]. The protector's
// purpose includes the file id, so frames cannot be moved between files; the sequence catches dropped or reordered
// frames. Kinds: 1 header (JSON), 2 rows (gzip of RowCodec rows), 3 trailer (JSON: row count + RowChecksum). A file
// without its trailer is incomplete and never counts as a backup.
internal static class SnapshotFormat
{
    public static readonly byte[] Magic = "AITDBK2\n"u8.ToArray();

    public const byte HeaderFrame = 1;
    public const byte RowsFrame = 2;
    public const byte TrailerFrame = 3;

    // A rows frame is flushed at this many rows or bytes (before compression), whichever comes first.
    public const int FrameRows = 2000;
    public const int FrameBytes = 1 << 20;

    public const int MaxFrameBytes = 64 << 20;

    public static IDataProtector For(IDataProtector root, Guid fileId) => root.CreateProtector("file:" + fileId.ToString("N"));

    public sealed record HeaderJson(int Version, string? Schema, string Table, List<SnapshotColumn> Columns, BackupScopeKind Scope, string? Source, string? Filter,
        DateTime TakenAt)
    {
        public static HeaderJson From(SnapshotHeader header) =>
            new(2, header.Table.Schema, header.Table.Name, header.Columns.ToList(), header.Scope.Kind, header.Scope.Source, header.Scope.Filter, header.TakenAt);

        public SnapshotHeader ToHeader() => new(new TableName(Schema, Table), Columns, new BackupScope(Scope, Source, Filter), TakenAt);
    }

    public sealed record TrailerJson(long Rows, string Checksum);
}

public sealed record SnapshotSummary(long Rows, string Checksum, long Bytes);

// Writes one backup file row by row. reserve: bytes the disk must keep free; expectedRows (from COUNT(*)) lets the
// writer stop early when the rest of the table would not fit. The caller deletes the file when anything fails.
public sealed class SnapshotWriter : IAsyncDisposable
{
    private readonly FileStream _file;
    private readonly IDataProtector _protector;
    private readonly long _reserve;
    private readonly long? _expectedRows;
    private readonly RowChecksum _checksum;
    private readonly MemoryStream _buffer = new();
    private readonly BinaryWriter _rows;
    private int _frameRows;
    private int _sequence;

    private SnapshotWriter(FileStream file, IDataProtector protector, SnapshotHeader header, long reserve, long? expectedRows)
    {
        _file = file;
        _protector = protector;
        _reserve = reserve;
        _expectedRows = expectedRows;
        _checksum = new RowChecksum(header);
        _rows = new BinaryWriter(_buffer, System.Text.Encoding.UTF8, leaveOpen: true);
    }

    public string Path => _file.Name;

    public long Rows => _checksum.Count;

    public static async Task<SnapshotWriter> CreateAsync(string path, IDataProtector root, SnapshotHeader header, long reserve, long? expectedRows,
        CancellationToken cancellationToken)
    {
        DiskSpace.Ensure(path, 0, reserve, "the backup");
        var fileId = Guid.NewGuid();
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var writer = new SnapshotWriter(file, SnapshotFormat.For(root, fileId), header, reserve, expectedRows);
        try
        {
            await file.WriteAsync(SnapshotFormat.Magic, cancellationToken);
            await file.WriteAsync(fileId.ToByteArray(), cancellationToken);
            await writer.WriteFrameAsync(SnapshotFormat.HeaderFrame, JsonSerializer.SerializeToUtf8Bytes(SnapshotFormat.HeaderJson.From(header)), cancellationToken);
            return writer;
        }
        catch
        {
            await writer.DisposeAsync();
            throw;
        }
    }

    public async Task WriteAsync(Cell[] row, CancellationToken cancellationToken)
    {
        _checksum.Add(row);
        RowCodec.Write(_rows, row);
        _frameRows++;
        if (_frameRows >= SnapshotFormat.FrameRows || _buffer.Length >= SnapshotFormat.FrameBytes) await FlushRowsAsync(cancellationToken);
    }

    // Writes the last rows and the trailer and forces the file to disk.
    public async Task<SnapshotSummary> CompleteAsync(CancellationToken cancellationToken)
    {
        await FlushRowsAsync(cancellationToken);
        var checksum = _checksum.Value;
        await WriteFrameAsync(SnapshotFormat.TrailerFrame, JsonSerializer.SerializeToUtf8Bytes(new SnapshotFormat.TrailerJson(_checksum.Count, checksum)), cancellationToken);
        await _file.FlushAsync(cancellationToken);
        _file.Flush(flushToDisk: true);
        return new SnapshotSummary(_checksum.Count, checksum, _file.Length);
    }

    private async Task FlushRowsAsync(CancellationToken cancellationToken)
    {
        if (_frameRows == 0) return;
        _rows.Flush();
        using var compressed = new MemoryStream();
        await using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(BitConverter.GetBytes(_frameRows));
            _buffer.Position = 0;
            await _buffer.CopyToAsync(gzip, cancellationToken);
        }
        _buffer.SetLength(0);
        _frameRows = 0;
        await WriteFrameAsync(SnapshotFormat.RowsFrame, compressed.ToArray(), cancellationToken);

        // What the rest of the rows will take, at the size per row so far.
        var written = _checksum.Count;
        var remaining = _expectedRows is { } expected && expected > written && written > 0 ? (long)((double)_file.Length / written * (expected - written)) : 0;
        DiskSpace.Ensure(_file.Name, remaining, _reserve, "the backup");
    }

    private async Task WriteFrameAsync(byte kind, byte[] body, CancellationToken cancellationToken)
    {
        var plain = new byte[5 + body.Length];
        BitConverter.TryWriteBytes(plain.AsSpan(0, 4), _sequence++);
        plain[4] = kind;
        body.CopyTo(plain, 5);
        var payload = _protector.Protect(plain);
        await _file.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken);
        await _file.WriteAsync(payload, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _rows.DisposeAsync();
        await _buffer.DisposeAsync();
        await _file.DisposeAsync();
    }
}

// Reads a backup file back as a stream. Rows are checked as they come (frame authenticity and order); the count and
// checksum at the end are compared with the trailer and with the expected checksum (the DatabaseBackups record), so
// a consumer that reads to the end has read exactly what was backed up, or gets a DatabaseToolException.
public static class SnapshotReader
{
    public static async Task<SnapshotHeader> ReadHeaderAsync(string path, IDataProtector root, CancellationToken cancellationToken)
    {
        await using var frames = await FrameReader.OpenAsync(path, root, cancellationToken);
        return await frames.HeaderAsync(cancellationToken);
    }

    public static async IAsyncEnumerable<Cell[]> ReadRowsAsync(string path, IDataProtector root, string? expectedChecksum,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var frames = await FrameReader.OpenAsync(path, root, cancellationToken);
        var header = await frames.HeaderAsync(cancellationToken);
        var checksum = new RowChecksum(header);
        while (true)
        {
            var (kind, body) = await frames.NextAsync(cancellationToken) ?? throw new DatabaseToolException("the backup file is incomplete (it has no end marker), so it cannot be trusted");
            if (kind == SnapshotFormat.TrailerFrame)
            {
                var trailer = JsonSerializer.Deserialize<SnapshotFormat.TrailerJson>(body) ?? throw Damaged("the end marker is empty");
                if (trailer.Rows != checksum.Count || trailer.Checksum != checksum.Value || (expectedChecksum is not null && expectedChecksum != checksum.Value))
                    throw new DatabaseToolException("the backup file does not match its checksum; it may have been changed");
                if (await frames.NextAsync(cancellationToken) is not null) throw Damaged("there is data after the end marker");
                yield break;
            }
            if (kind != SnapshotFormat.RowsFrame) throw Damaged($"unexpected frame kind {kind}");
            foreach (var row in Decode(body))
            {
                checksum.Add(row);
                yield return row;
            }
        }
    }

    private static List<Cell[]> Decode(byte[] body)
    {
        try
        {
            using var gzip = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, System.Text.Encoding.UTF8);
            var count = reader.ReadInt32();
            if (count is < 0 or > SnapshotFormat.FrameRows) throw new InvalidDataException("a frame has an impossible row count");
            var rows = new List<Cell[]>(count);
            for (var i = 0; i < count; i++) rows.Add(RowCodec.Read(reader));
            return rows;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        {
            throw Damaged(ex.Message);
        }
    }

    private static DatabaseToolException Damaged(string why) => new($"the backup file is damaged: {why}");

    private sealed class FrameReader : IAsyncDisposable
    {
        private readonly FileStream _file;
        private readonly IDataProtector _protector;
        private int _sequence;

        private FrameReader(FileStream file, IDataProtector protector)
        {
            _file = file;
            _protector = protector;
        }

        public static async Task<FrameReader> OpenAsync(string path, IDataProtector root, CancellationToken cancellationToken)
        {
            if (!File.Exists(path)) throw new DatabaseToolException($"the backup file is gone ({path})");
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            try
            {
                var start = new byte[SnapshotFormat.Magic.Length + 16];
                var read = await file.ReadAtLeastAsync(start, start.Length, throwOnEndOfStream: false, cancellationToken);
                if (read < start.Length || !start.AsSpan(0, SnapshotFormat.Magic.Length).SequenceEqual(SnapshotFormat.Magic))
                    throw new DatabaseToolException("the backup file is not in the format of this AIT version (it was made before backups were streamed), so it cannot be restored automatically");
                var fileId = new Guid(start.AsSpan(SnapshotFormat.Magic.Length, 16));
                return new FrameReader(file, SnapshotFormat.For(root, fileId));
            }
            catch
            {
                await file.DisposeAsync();
                throw;
            }
        }

        public async Task<SnapshotHeader> HeaderAsync(CancellationToken cancellationToken)
        {
            var (kind, body) = await NextAsync(cancellationToken) ?? throw Damaged("it is empty");
            if (kind != SnapshotFormat.HeaderFrame) throw Damaged("it does not start with a header");
            var header = JsonSerializer.Deserialize<SnapshotFormat.HeaderJson>(body) ?? throw Damaged("the header is empty");
            return header.ToHeader();
        }

        // Null at the end of the file.
        public async Task<(byte Kind, byte[] Body)?> NextAsync(CancellationToken cancellationToken)
        {
            var length = new byte[4];
            var read = await _file.ReadAtLeastAsync(length, 4, throwOnEndOfStream: false, cancellationToken);
            if (read == 0) return null;
            if (read < 4) throw Damaged("it ends inside a frame");
            var size = BitConverter.ToInt32(length);
            if (size is <= 0 or > SnapshotFormat.MaxFrameBytes) throw Damaged("a frame has an impossible size");
            var payload = new byte[size];
            if (await _file.ReadAtLeastAsync(payload, size, throwOnEndOfStream: false, cancellationToken) < size) throw Damaged("it ends inside a frame");

            byte[] plain;
            try
            {
                plain = _protector.Unprotect(payload);
            }
            catch (CryptographicException ex)
            {
                throw new DatabaseToolException("the backup file cannot be decrypted on this machine (it was made with another key, or it was changed)", ex);
            }
            if (plain.Length < 5 || BitConverter.ToInt32(plain, 0) != _sequence++) throw Damaged("its frames are out of order");
            return (plain[4], plain[5..]);
        }

        public ValueTask DisposeAsync() => _file.DisposeAsync();
    }
}
