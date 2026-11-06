using System.Data.Common;
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

// The rows of one table as they were: the content of a backup and of a rehearsal copy. Rows follow Columns.
public sealed class TableSnapshot
{
    public required TableName Table { get; init; }
    public required IReadOnlyList<SnapshotColumn> Columns { get; init; }
    public required List<Cell[]> Rows { get; init; }
    public DateTime TakenAt { get; init; } = DateTime.UtcNow;

    public IReadOnlyList<int> KeyIndexes => Columns.Select((c, i) => (c, i)).Where(x => x.c.IsKey).Select(x => x.i).ToList();

    public IReadOnlyList<int> CompareIndexes => Columns.Select((c, i) => (c, i)).Where(x => x.c.Writable || x.c.IsKey).Select(x => x.i).ToList();

    public string KeyOf(Cell[] row) => string.Join("\u001f", KeyIndexes.Select(i => row[i].Canonical));

    public string RowHash(Cell[] row) => Hash(string.Join("\u001f", CompareIndexes.Select(i => Columns[i].Name + "=" + row[i].Canonical)));

    // Over the rows sorted by key: equal for two snapshots that hold the same data, whatever order it was read in.
    public string Checksum()
    {
        var lines = Rows.Select(r => KeyOf(r) + "\u001e" + RowHash(r)).Order(StringComparer.Ordinal);
        return Hash(string.Join("\n", lines));
    }

    public static async Task<List<Cell[]>> ReadRowsAsync(DbDataReader reader, int columns, int maxRows, CancellationToken cancellationToken)
    {
        var rows = new List<Cell[]>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count >= maxRows) throw new DatabaseToolException($"the table has more than {maxRows} rows, the most a backup or rehearsal copy may hold");
            var row = new Cell[columns];
            for (var i = 0; i < columns; i++) row[i] = Cell.From(await reader.IsDBNullAsync(i, cancellationToken) ? null : reader.GetValue(i));
            rows.Add(row);
        }
        return rows;
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
