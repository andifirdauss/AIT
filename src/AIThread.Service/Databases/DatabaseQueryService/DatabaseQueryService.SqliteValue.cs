namespace AIThread.Service.Databases;

public sealed partial class DatabaseQueryService
{
    // SQLite keeps text, integers, reals and blobs: dates, GUIDs and decimals go as invariant text.
    private static object? SqliteValue(Cell cell) => cell.Tag switch
    {
        "null" => null,
        "b" => cell.Text == "1" ? 1L : 0L,
        "i" or "u" => long.TryParse(cell.Text, out var n) ? n : cell.Text,
        "d" or "f" => double.Parse(cell.Text!, System.Globalization.CultureInfo.InvariantCulture),
        "x" => Convert.FromBase64String(cell.Text!),
        _ => cell.Text
    };
}
