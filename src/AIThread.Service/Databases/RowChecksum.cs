using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Databases;

// A checksum over a set of rows that does not depend on their order: each row's key + content hash (SHA-256) is added
// into four 64-bit lanes, so rows can be read in any order and nothing has to be sorted or held in memory. Two reads of
// the same rows (same shape) give the same Value; the row count is part of it.
public sealed class RowChecksum(SnapshotHeader shape)
{
    private ulong _a;
    private ulong _b;
    private ulong _c;
    private ulong _d;

    public long Count { get; private set; }

    public void Add(Cell[] row)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(shape.KeyOf(row) + "\u001e" + shape.RowHash(row)), hash);
        unchecked
        {
            _a += BinaryPrimitives.ReadUInt64LittleEndian(hash);
            _b += BinaryPrimitives.ReadUInt64LittleEndian(hash[8..]);
            _c += BinaryPrimitives.ReadUInt64LittleEndian(hash[16..]);
            _d += BinaryPrimitives.ReadUInt64LittleEndian(hash[24..]);
        }
        Count++;
    }

    // 64 hex characters (the DatabaseBackups.Checksum column).
    public string Value
    {
        get
        {
            Span<byte> state = stackalloc byte[40];
            BinaryPrimitives.WriteUInt64LittleEndian(state, _a);
            BinaryPrimitives.WriteUInt64LittleEndian(state[8..], _b);
            BinaryPrimitives.WriteUInt64LittleEndian(state[16..], _c);
            BinaryPrimitives.WriteUInt64LittleEndian(state[24..], _d);
            BinaryPrimitives.WriteInt64LittleEndian(state[32..], Count);
            return Convert.ToHexStringLower(SHA256.HashData(state));
        }
    }

    public static async Task<RowChecksum> OfAsync(SnapshotHeader shape, IAsyncEnumerable<Cell[]> rows, CancellationToken cancellationToken)
    {
        var checksum = new RowChecksum(shape);
        await foreach (var row in rows.WithCancellation(cancellationToken)) checksum.Add(row);
        return checksum;
    }
}
