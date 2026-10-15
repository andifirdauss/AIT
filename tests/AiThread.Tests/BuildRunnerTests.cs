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

    [Fact]
    public async Task Missing_visual_studio_targets_are_an_environment_problem()
    {
        if (!OperatingSystem.IsWindows()) return;

        _workspace.Write("fail.cmd", """
            @echo D:\src\Web\Web.csproj(2586,3): error MSB4019: The imported project "C:\Program Files\dotnet\sdk\10.0.401\Microsoft\VisualStudio\v18.0\WebApplications\Microsoft.WebApplication.targets" was not found. [D:\src\Web\Web.csproj]
            @exit /b 1
            """);

        var result = await _runner.RunAsync(Command("cmd.exe", "/d", "/c", Path.Combine(_workspace.Root, "fail.cmd")), TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("MSB4019", Assert.Single(result.Errors).Code);
        Assert.NotNull(result.StartError);
        Assert.Contains("not a code problem", result.StartError);
    }

    [Fact]
    public void Output_held_by_another_handle_is_in_use()
    {
        _workspace.Write("App/bin/Debug/App.dll", "binary");
        _workspace.Write("App/bin/Debug/readme.txt", "not a binary");
        var dll = Path.Combine(_workspace.Root, "App", "bin", "Debug", "App.dll");
        var bin = Path.Combine(_workspace.Root, "App", "bin");

        Assert.Null(BuildRunner.FirstInUse([bin, Path.Combine(_workspace.Root, "Missing", "bin")]));
        using (new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(dll, BuildRunner.FirstInUse([bin]));
    }

    // A running app holds its binaries: the rebuild must not clean bin/ around them (runtimeconfig.json would be
    // deleted and not copied back, so the app could no longer start).
    [Fact]
    public async Task Running_app_is_built_without_clean()
    {
        if (!OperatingSystem.IsWindows()) return;

        _workspace.Write("Live/Live.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        _workspace.Write("Live/Program.cs", "System.Threading.Thread.Sleep(60000);");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["Live/Live.csproj"], null, out _);
        Assert.NotNull(command?.Incremental);
        Assert.True((await _runner.RunAsync(command, TimeSpan.FromMinutes(3), CancellationToken.None)).Succeeded);

        var output = Path.Combine(_workspace.Root, "Live", "bin", "Debug", "net10.0");
        using var app = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(output, "Live.exe")) { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            await Task.Delay(1000);
            _workspace.Write("Live/Program.cs", "System.Threading.Thread.Sleep(60001);");

            var result = await _runner.RunAsync(command, TimeSpan.FromMinutes(3), CancellationToken.None);

            Assert.NotNull(result.InUseOutput);
            Assert.True(result.Passed, result.OutputTail);
            Assert.True(File.Exists(Path.Combine(output, "Live.runtimeconfig.json")));
        }
        finally
        {
            app.Kill();
            await app.WaitForExitAsync();

            // Windows releases the image a moment after the exit; the workspace is deleted next.
            for (var i = 0; i < 50 && BuildRunner.FirstInUse([output]) is not null; i++) await Task.Delay(100);
        }
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
