using AIThread.UI.Hosting;

namespace AIThread.Tests;

public class AutostartTests
{
    private const string Exe = @"C:\Tools\AIThread.exe";
    private const string Conhost = @"C:\Windows\System32\conhost.exe";

    [Fact]
    public void The_entry_starts_the_tray_in_a_console_nobody_sees() =>
        Assert.Equal($"\"{Conhost}\" --headless \"{Exe}\" tray", Autostart.Command(Exe, Conhost));

    [Fact]
    public void Without_a_headless_console_host_the_entry_stays_plain() =>
        Assert.Equal($"\"{Exe}\" tray", Autostart.Command(Exe, null));

    [Fact]
    public void An_old_entry_of_this_exe_is_upgraded() =>
        Assert.True(Autostart.NeedsUpgrade($"\"{Exe}\" tray", Exe, Conhost));

    [Theory]
    [InlineData(null)]
    [InlineData("\"D:\\Other\\AIThread.exe\" tray")]
    public void No_entry_or_another_install_is_left_alone(string? existing) =>
        Assert.False(Autostart.NeedsUpgrade(existing, Exe, Conhost));

    [Fact]
    public void An_entry_that_is_already_headless_is_left_alone() =>
        Assert.False(Autostart.NeedsUpgrade(Autostart.Command(Exe, Conhost), Exe, Conhost));
}
