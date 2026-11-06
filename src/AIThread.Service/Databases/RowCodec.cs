namespace AIThread.Service.Databases;

// One row as bytes (backup frames, comparison files): per cell its type tag and its invariant text, so a restore
// writes back the same .NET type it read.
public static class RowCodec
{
    public static void Write(BinaryWriter writer, Cell[] row)
    {
        writer.Write7BitEncodedInt(row.Length);
        foreach (var cell in row)
        {
            writer.Write(cell.Tag);
            writer.Write(cell.Text is not null);
            if (cell.Text is not null) writer.Write(cell.Text);
        }
    }

    public static Cell[] Read(BinaryReader reader)
    {
        var width = reader.Read7BitEncodedInt();
        if (width is < 0 or > 100_000) throw new InvalidDataException("a row has an impossible width");
        var row = new Cell[width];
        for (var i = 0; i < width; i++)
        {
            var tag = reader.ReadString();
            var text = reader.ReadBoolean() ? reader.ReadString() : null;
            row[i] = tag == "null" ? Cell.Null : new Cell(tag, text);
        }
        return row;
    }

    public static byte[] ToBytes(Cell[] row)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true)) Write(writer, row);
        return buffer.ToArray();
    }

    public static Cell[] FromBytes(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), System.Text.Encoding.UTF8);
        return Read(reader);
    }
}
