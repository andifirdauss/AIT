using System.IO.Compression;
using System.Security.Cryptography;
using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Testing;

public sealed partial class TestBrowserLauncher
{
    // Downloads the official Node.js build once into %LOCALAPPDATA%\AIThread\node, checked against the SHA-256 list
    // nodejs.org publishes next to it. Only node.exe is kept.
    private async Task<string> DownloadNodeAsync(Func<string, Task> notice, CancellationToken cancellationToken)
    {
        var archive = $"node-v{NodeVersion}-win-x64";
        var baseUrl = $"https://nodejs.org/dist/v{NodeVersion}/";
        var zipPath = Path.Combine(Path.GetTempPath(), $"aithread-{archive}-{Guid.NewGuid():N}.zip");
        await notice(Lang.T($"Node.js untuk Playwright tidak ditemukan — mengunduh Node.js {NodeVersion} (sekali saja, ±36 MB)", $"Node.js for Playwright was not found — downloading Node.js {NodeVersion} (only once, ±36 MB)"));
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            var sums = await client.GetStringAsync(baseUrl + "SHASUMS256.txt", cancellationToken);
            var expected = sums.Split('\n')
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .FirstOrDefault(parts => parts.Length == 2 && parts[1] == archive + ".zip")?[0]
                ?? throw new InvalidDataException(Lang.T($"checksum {archive}.zip tidak ada di SHASUMS256.txt", $"the checksum of {archive}.zip is not in SHASUMS256.txt"));

            await using (var source = await client.GetStreamAsync(baseUrl + archive + ".zip", cancellationToken))
            await using (var target = File.Create(zipPath))
                await source.CopyToAsync(target, cancellationToken);

            await using (var stream = File.OpenRead(zipPath))
            {
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Lang.T("isi unduhan tidak cocok dengan checksum nodejs.org", "the download does not match the nodejs.org checksum"));
            }

            // Extracted beside the final name first, so an interrupted run never leaves a broken node.exe behind.
            Directory.CreateDirectory(Path.GetDirectoryName(DownloadedNode)!);
            var partial = DownloadedNode + ".part";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var entry = zip.GetEntry($"{archive}/node.exe") ?? throw new InvalidDataException(Lang.T("node.exe tidak ada di arsip", "node.exe is not in the archive"));
                entry.ExtractToFile(partial, overwrite: true);
            }
            File.Move(partial, DownloadedNode, overwrite: true);
            return DownloadedNode;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException
                                   || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Downloading Node.js {Version} for Playwright failed", NodeVersion);
            throw new InvalidOperationException(
                Lang.T($"Node.js untuk Playwright tidak ditemukan dan gagal diunduh ({ex.Message}); pasang Node.js {MinNodeMajor}+ lalu ulangi testing", $"Node.js for Playwright was not found and could not be downloaded ({ex.Message}); install Node.js {MinNodeMajor}+ and run the testing again"), ex);
        }
        finally
        {
            try { File.Delete(zipPath); }
            catch (IOException) { }
        }
    }
}
