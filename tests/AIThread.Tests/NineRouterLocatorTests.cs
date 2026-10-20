using AIThread.Service.Implementations;

namespace AIThread.Tests;

public class NineRouterLocatorTests
{
    private const string AppData = @"C:\Users\dev\AppData\Roaming";
    private const string GlobalCli = @"C:\Users\dev\AppData\Roaming\npm\node_modules\9router\cli.js";
    private const string ExeDir = @"C:\Users\dev\AppData\Roaming\npm\node_modules\aithread\node_modules\aithread-win-x64\bin";
    private const string BundledCli = @"C:\Users\dev\AppData\Roaming\npm\node_modules\aithread\node_modules\9router\cli.js";

    private static Func<string, bool> Files(params string[] paths) =>
        path => paths.Contains(path, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Global_install_wins_over_the_copy_bundled_with_aithread()
    {
        var cli = NineRouterLocator.FindCli(null, [@"C:\Windows", @"C:\Users\dev\AppData\Roaming\npm"], AppData, ExeDir,
            Files(@"C:\Users\dev\AppData\Roaming\npm\9router.cmd", GlobalCli, BundledCli));

        Assert.Equal(new NineRouterCli(GlobalCli, NineRouterSource.Global), cli);
    }

    [Fact]
    public void Global_prefix_on_path_is_found_outside_appdata()
    {
        const string cli = @"D:\node-global\node_modules\9router\cli.js";

        var found = NineRouterLocator.FindCli(null, [@"D:\node-global"], AppData, @"D:\tools",
            Files(@"D:\node-global\9router.cmd", cli));

        Assert.Equal(new NineRouterCli(cli, NineRouterSource.Global), found);
    }

    [Fact]
    public void Bundled_copy_is_used_after_the_global_one_is_uninstalled()
    {
        var cli = NineRouterLocator.FindCli(null, [@"C:\Users\dev\AppData\Roaming\npm"], AppData, ExeDir, Files(BundledCli));

        Assert.Equal(new NineRouterCli(BundledCli, NineRouterSource.Bundled), cli);
    }

    [Fact]
    public void Configured_path_wins_and_a_missing_one_falls_back()
    {
        const string configured = @"E:\9router\cli.js";

        Assert.Equal(NineRouterSource.Configured,
            NineRouterLocator.FindCli(configured, [], AppData, ExeDir, Files(configured, GlobalCli))!.Source);
        Assert.Equal(NineRouterSource.Global,
            NineRouterLocator.FindCli(configured, [], AppData, ExeDir, Files(GlobalCli))!.Source);
    }

    [Fact]
    public void Nothing_installed_is_null()
    {
        Assert.Null(NineRouterLocator.FindCli(null, [@"C:\Windows"], AppData, ExeDir, Files()));
    }

    [Fact]
    public void Executable_is_taken_from_the_first_directory_that_has_it()
    {
        var node = NineRouterLocator.FindExecutable("node.exe", null, [@"C:\a", @"C:\b", @"C:\c"],
            Files(@"C:\b\node.exe", @"C:\c\node.exe"));

        Assert.Equal(@"C:\b\node.exe", node);
        Assert.Null(NineRouterLocator.FindExecutable("node.exe", null, [@"C:\a"], Files()));
    }

    [Theory]
    [InlineData("http://localhost:20128", true, 20128)]
    [InlineData("http://127.0.0.1:20128/v1", true, 20128)]
    [InlineData("http://[::1]:3000", true, 3000)]
    [InlineData("http://localhost", true, 80)]
    [InlineData("http://192.168.1.10:20128", false, 0)]
    [InlineData("https://router.example.com", false, 0)]
    [InlineData("not a url", false, 0)]
    public void Only_local_urls_can_be_started(string url, bool local, int port)
    {
        Assert.Equal(local, NineRouterLocator.TryGetLocalPort(url, out var actual));
        Assert.Equal(port, actual);
    }
}
