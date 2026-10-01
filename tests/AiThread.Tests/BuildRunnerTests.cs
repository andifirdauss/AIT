using AiThread.Service.Verification;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiThread.Tests;

public class BuildRunnerTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly BuildRunner _runner = new(NullLogger<BuildRunner>.Instance);

    public void Dispose() => _workspace.Dispose();

    private BuildCommand Command(string fileName, params string[] arguments) =>
        new("test", fileName, arguments, _workspace.Root, $"{fileName} {string.Join(' ', arguments)}", _workspace.Root);

    [Fact]
    public async Task Successful_command_reports_exit_code_and_output()
    {
        var result = await _runner.RunAsync(Command("dotnet", "--version"), TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.True(result.Succeeded, result.OutputTail);
        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"\d+\.\d+\.\d+", result.OutputTail);
    }

    [Fact]
    public async Task Missing_program_is_reported_instead_of_thrown()
    {
        var result = await _runner.RunAsync(Command("aithread-no-such-tool-xyz"), TimeSpan.FromSeconds(10), CancellationToken.None);

        Assert.False(result.Passed);
        Assert.NotNull(result.StartError);
    }

    [Fact]
    public async Task Timeout_stops_the_process()
    {
        if (!OperatingSystem.IsWindows()) return;

        var result = await _runner.RunAsync(Command("cmd.exe", "/d", "/c", "ping", "-n", "30", "127.0.0.1"), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Passed);
        Assert.InRange(result.DurationMs, 0, 15_000);
    }

    [Fact]
    public async Task Cancellation_stops_the_process_and_throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync(Command("cmd.exe", "/d", "/c", "ping", "-n", "30", "127.0.0.1"), TimeSpan.FromMinutes(1), cancellation.Token));
    }

    // Real dotnet build of a tiny broken project: checks the command line and the parser against actual SDK output.
    [Fact]
    public async Task Dotnet_build_errors_come_back_with_file_and_line()
    {
        _workspace.Write("Broken/Broken.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        _workspace.Write("Broken/Program.cs", """
            public static class Program
            {
                public static void Main()
                {
                    Console.WriteLine("missing using");
                }
            }
            """);

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["Broken/Broken.csproj"], null, out _);
        Assert.NotNull(command);

        var result = await _runner.RunAsync(command, TimeSpan.FromMinutes(3), CancellationToken.None);

        Assert.False(result.Passed, result.OutputTail);
        var error = Assert.Single(result.Errors);
        Assert.Equal("CS0103", error.Code);
        Assert.Equal(Path.Combine(_workspace.Root, "Broken", "Program.cs"), error.File, ignoreCase: true);
        Assert.Equal(5, error.Line);
    }
}
