using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AIThread.Service.Databases;

// Compares two sets of rows of the same shape without holding either in memory: each side goes into a table of a
// temporary SQLite file (b = the backup or "before", a = the current rows), indexed by key, and SQLite finds rows only
// in one side and rows whose content differs. Nothing readable is on disk: the key is a hash, the content hash says
// nothing, and the row itself is encrypted with a key that lives only in this object. The file is deleted on dispose.
public sealed class DiffScratch : IAsyncDisposable
{
    public enum Side
    {
        Backup,
        Current
    }

    private const int CommitEvery = 5000;

    private readonly SqliteConnection _db;
    private readonly string _path;
    private readonly SnapshotHeader _shape;
    private readonly long _reserve;
    private readonly AesGcm _aes;
    private long _nonce;

    private DiffScratch(SqliteConnection db, string path, SnapshotHeader shape, long reserve)
    {
        _db = db;
        _path = path;
        _shape = shape;
        _reserve = reserve;
        _aes = new AesGcm(RandomNumberGenerator.GetBytes(32), 16);
    }

    public static async Task<DiffScratch> CreateAsync(string folder, SnapshotHeader shape, long reserve, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        DeleteStale(folder);
        DiskSpace.Ensure(folder, 0, reserve, "the comparison file");
        var path = Path.Combine(folder, $"{Guid.NewGuid():N}.db");
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        var scratch = new DiffScratch(db, path, shape, reserve);
        try
        {
            await db.OpenAsync(cancellationToken);
            await scratch.ExecAsync("""
                PRAGMA journal_mode = OFF;
                PRAGMA synchronous = OFF;
                PRAGMA temp_store = FILE;
                PRAGMA cache_size = -8000;
                CREATE TABLE b (k BLOB PRIMARY KEY, h TEXT NOT NULL, r BLOB NOT NULL);
                CREATE TABLE a (k BLOB PRIMARY KEY, h TEXT NOT NULL, r BLOB NOT NULL);
                """, cancellationToken);
            return scratch;
        }
        catch
        {
            await scratch.DisposeAsync();
            throw;
        }
    }

    public async Task<long> LoadAsync(Side side, IAsyncEnumerable<Cell[]> rows, CancellationToken cancellationToken)
    {
        var count = 0L;
        var transaction = (SqliteTransaction)await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var insert = _db.CreateCommand();
            insert.CommandText = $"INSERT INTO {Table(side)} (k, h, r) VALUES ($k, $h, $r)";
            var k = insert.Parameters.Add("$k", SqliteType.Blob);
            var h = insert.Parameters.Add("$h", SqliteType.Text);
            var r = insert.Parameters.Add("$r", SqliteType.Blob);
            insert.Transaction = transaction;

            await foreach (var row in rows.WithCancellation(cancellationToken))
            {
                k.Value = KeyHash(row);
                h.Value = _shape.RowHash(row);
                r.Value = Seal(row);
                try
                {
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    throw new DatabaseToolException($"two rows of '{_shape.Table.Display}' have the same primary key value, so they cannot be told apart for a restore", ex);
                }
                if (++count % CommitEvery != 0) continue;
                await transaction.CommitAsync(cancellationToken);
                await transaction.DisposeAsync();
                DiskSpace.Ensure(_path, 0, _reserve, "the comparison file");
                transaction = (SqliteTransaction)await _db.BeginTransactionAsync(cancellationToken);
                insert.Transaction = transaction;
            }
            await transaction.CommitAsync(cancellationToken);
            return count;
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    public Task ClearAsync(Side side, CancellationToken cancellationToken) => ExecAsync($"DELETE FROM {Table(side)}", cancellationToken);

    // Rows (decrypted) whose key is only in side.
    public IAsyncEnumerable<Cell[]> OnlyInAsync(Side side, CancellationToken cancellationToken) =>
        ReadAsync($"SELECT x.r FROM {Table(side)} x WHERE NOT EXISTS (SELECT 1 FROM {Table(Other(side))} y WHERE y.k = x.k)", cancellationToken);

    public Task<long> CountOnlyInAsync(Side side, CancellationToken cancellationToken) =>
        ScalarAsync($"SELECT COUNT(*) FROM {Table(side)} x WHERE NOT EXISTS (SELECT 1 FROM {Table(Other(side))} y WHERE y.k = x.k)", cancellationToken);

    // Rows with the same key in both sides but different content: (backup, current).
    public async IAsyncEnumerable<(Cell[] Before, Cell[] After)> ChangedAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = _db.CreateCommand();
        command.CommandText = "SELECT b.r, a.r FROM b JOIN a ON a.k = b.k WHERE a.h <> b.h";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) yield return (Open((byte[])reader[0]), Open((byte[])reader[1]));
    }

    public Task<long> CountChangedAsync(CancellationToken cancellationToken) => ScalarAsync("SELECT COUNT(*) FROM b JOIN a ON a.k = b.k WHERE a.h <> b.h", cancellationToken);

    private async IAsyncEnumerable<Cell[]> ReadAsync(string sql, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var command = _db.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) yield return Open((byte[])reader[0]);
    }

    private async Task<long> ScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task ExecAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _db.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private byte[] KeyHash(Cell[] row) => SHA256.HashData(Encoding.UTF8.GetBytes(_shape.KeyOf(row)))[..16];

    // nonce (12) · tag (16) · ciphertext
    private byte[] Seal(Cell[] row)
    {
        var plain = RowCodec.ToBytes(row);
        var sealedRow = new byte[28 + plain.Length];
        var nonce = sealedRow.AsSpan(0, 12);
        BitConverter.TryWriteBytes(nonce[4..], Interlocked.Increment(ref _nonce));
        _aes.Encrypt(nonce, plain, sealedRow.AsSpan(28), sealedRow.AsSpan(12, 16));
        return sealedRow;
    }

    private Cell[] Open(byte[] sealedRow)
    {
        var plain = new byte[sealedRow.Length - 28];
        _aes.Decrypt(sealedRow.AsSpan(0, 12), sealedRow.AsSpan(28), sealedRow.AsSpan(12, 16), plain);
        return RowCodec.FromBytes(plain);
    }

    // Files left by a process that ended mid-comparison (their key died with it).
    private static void DeleteStale(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*.db"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-1)) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open elsewhere; next time.
            }
        }
    }

    private static string Table(Side side) => side == Side.Backup ? "b" : "a";

    private static Side Other(Side side) => side == Side.Backup ? Side.Current : Side.Backup;

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        _aes.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The content is encrypted with a key that is gone; a later start may delete it.
        }
    }
}
