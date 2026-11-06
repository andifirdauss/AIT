using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AIThread.Service.Databases;

// One column of a table: Writable false for what a restore cannot (and a checksum must not) include: identity values
// are writable (restored explicitly), computed columns and row versions are not.
public sealed record SnapshotColumn(string Name, string Type, bool IsKey, bool IsIdentity, bool Writable, bool AllowNull);

// A cell kept with its .NET type, so a restore writes the same type back: Tag names the type, Text is invariant.
public sealed record Cell(string Tag, string? Text)
{
    public static readonly Cell Null = new("null", null);

    public static Cell From(object? value) => value switch
    {
        null or DBNull => Null,
        string s => new Cell("s", s),
        bool b => new Cell("b", b ? "1" : "0"),
        byte or sbyte or short or ushort or int or uint or long => new Cell("i", Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)),
        ulong u => new Cell("u", u.ToString(CultureInfo.InvariantCulture)),
        decimal d => new Cell("m", d.ToString(CultureInfo.InvariantCulture)),
        double f => new Cell("d", f.ToString("R", CultureInfo.InvariantCulture)),
        float f => new Cell("f", f.ToString("R", CultureInfo.InvariantCulture)),
        DateTime t => new Cell("t", t.ToString("O", CultureInfo.InvariantCulture)),
        DateTimeOffset o => new Cell("o", o.ToString("O", CultureInfo.InvariantCulture)),
        DateOnly d => new Cell("da", d.ToString("O", CultureInfo.InvariantCulture)),
        TimeOnly t => new Cell("ti", t.ToString("O", CultureInfo.InvariantCulture)),
        TimeSpan span => new Cell("ts", span.ToString("c", CultureInfo.InvariantCulture)),
        Guid g => new Cell("g", g.ToString("D")),
        byte[] bytes => new Cell("x", Convert.ToBase64String(bytes)),
        char c => new Cell("s", c.ToString()),
        _ => new Cell("s", Convert.ToString(value, CultureInfo.InvariantCulture))
    };

    // The value to bind as a parameter.
    public object? ToValue() => Tag switch
    {
        "null" => null,
        "b" => Text == "1",
        "i" => long.Parse(Text!, CultureInfo.InvariantCulture),
        "u" => ulong.Parse(Text!, CultureInfo.InvariantCulture),
        "m" => decimal.Parse(Text!, CultureInfo.InvariantCulture),
        "d" => double.Parse(Text!, CultureInfo.InvariantCulture),
        "f" => float.Parse(Text!, CultureInfo.InvariantCulture),
        "t" => DateTime.Parse(Text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        "o" => DateTimeOffset.Parse(Text!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        "da" => DateOnly.Parse(Text!, CultureInfo.InvariantCulture),
        "ti" => TimeOnly.Parse(Text!, CultureInfo.InvariantCulture),
        "ts" => TimeSpan.Parse(Text!, CultureInfo.InvariantCulture),
        "g" => Guid.Parse(Text!),
        "x" => Convert.FromBase64String(Text!),
        _ => Text
    };

    // Compared across engines and drivers: a whole decimal and an integer, or a bool and 0/1, are the same value.
    public string Canonical => Tag switch
    {
        "null" => "␀",
        "m" when decimal.TryParse(Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d.ToString("0.############################", CultureInfo.InvariantCulture),
        "b" => Text!,
        _ => Text ?? string.Empty
    };
}


// The shape of a backup or a compared table: its columns in order (primary key marked) and what the backup covers.
// KeyOf and RowHash give the identity and the content of a row; a checksum is built from them (RowChecksum).
public sealed class SnapshotHeader
{
    private readonly int[] _keys;
    private readonly int[] _compare;

    public SnapshotHeader(TableName table, IReadOnlyList<SnapshotColumn> columns, BackupScope scope, DateTime takenAt)
    {
        Table = table;
        Columns = columns;
        Scope = scope;
        TakenAt = takenAt;
        _keys = columns.Select((c, i) => (c, i)).Where(x => x.c.IsKey).Select(x => x.i).ToArray();
        _compare = columns.Select((c, i) => (c, i)).Where(x => x.c.Writable || x.c.IsKey).Select(x => x.i).ToArray();
    }

    public TableName Table { get; }
    public IReadOnlyList<SnapshotColumn> Columns { get; }
    public BackupScope Scope { get; }
    public DateTime TakenAt { get; }

    public IReadOnlyList<int> KeyIndexes => _keys;

    // Writable columns and the key: what a restore writes back, so what a checksum covers.
    public IReadOnlyList<int> CompareIndexes => _compare;

    public bool HasKey => _keys.Length > 0;

    public string KeyOf(Cell[] row) => string.Join("\u001f", _keys.Select(i => row[i].Canonical));

    public string RowHash(Cell[] row) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", _compare.Select(i => Columns[i].Name + "=" + row[i].Canonical)))));

    public Cell[] KeyCells(Cell[] row)
    {
        var key = new Cell[_keys.Length];
        for (var n = 0; n < _keys.Length; n++) key[n] = row[_keys[n]];
        return key;
    }

    // The same table seen through its key columns only (in key order): the rows of an INSERT's key lists.
    public SnapshotHeader KeysOnly() => new(Table, _keys.Select(i => Columns[i]).ToList(), Scope, TakenAt);
}
