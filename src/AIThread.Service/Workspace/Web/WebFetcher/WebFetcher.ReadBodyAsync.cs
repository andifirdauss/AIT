namespace AIThread.Service.Workspace.Web;

public sealed partial class WebFetcher
{
    // At most MaxBodyBytes; Cut is true when the body went on.
    private static async Task<(byte[] Body, bool Cut)> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (buffer.Length < MaxBodyBytes)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, MaxBodyBytes - buffer.Length)), cancellationToken);
            if (read == 0) return (buffer.ToArray(), false);
            buffer.Write(chunk, 0, read);
        }
        return (buffer.ToArray(), await stream.ReadAsync(chunk.AsMemory(0, 1), cancellationToken) > 0);
    }
}
