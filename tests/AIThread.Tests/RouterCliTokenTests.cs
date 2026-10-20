using System.Security.Cryptography;
using System.Text;
using AIThread.Service.Implementations;

namespace AIThread.Tests;

// The CLI token that lets the quota lookup through 9router's "Require login" (the default) on this machine.
public class RouterCliTokenTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ait-9r-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Token_is_the_first_16_hex_of_sha256_over_machine_id_salt_and_secret()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("abc" + "9r-cli-auth" + "s3cret")))[..16];

        Assert.Equal(expected, RouterCliToken.Compute("abc", "s3cret"));
        Assert.Equal(16, expected.Length);
    }

    [Fact]
    public void Load_reads_trimmed_files_and_needs_the_machine_id()
    {
        Assert.Null(RouterCliToken.Load(_dir));

        Directory.CreateDirectory(Path.Combine(_dir, "auth"));
        File.WriteAllText(Path.Combine(_dir, "machine-id"), "abc\n");
        File.WriteAllText(Path.Combine(_dir, "auth", "cli-secret"), " s3cret\r\n");

        Assert.Equal(RouterCliToken.Compute("abc", "s3cret"), RouterCliToken.Load(_dir));
    }

    [Fact]
    public void Missing_secret_is_created_once_and_reused()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "machine-id"), "abc");

        var first = RouterCliToken.Load(_dir);
        var secret = File.ReadAllText(Path.Combine(_dir, "auth", "cli-secret"));

        Assert.Equal(64, secret.Length);
        Assert.Equal(RouterCliToken.Compute("abc", secret), first);
        Assert.Equal(first, RouterCliToken.Load(_dir));
    }

    [Fact]
    public void Data_directory_follows_9router()
    {
        Assert.Equal(@"D:\9r", RouterCliToken.DataDirectory(@"D:\9r", @"C:\Users\a\AppData\Roaming", @"C:\Users\a", isWindows: true));
        Assert.Equal(Path.Combine(@"C:\Users\a\AppData\Roaming", "9router"), RouterCliToken.DataDirectory(null, @"C:\Users\a\AppData\Roaming", @"C:\Users\a", isWindows: true));
        Assert.Equal(Path.Combine("/home/a", ".9router"), RouterCliToken.DataDirectory("", null, "/home/a", isWindows: false));
    }
}
