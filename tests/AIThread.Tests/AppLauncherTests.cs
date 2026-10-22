using AIThread.Service.Testing;
using AIThread.Service.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIThread.Tests;

// How AIT starts an app that is not running for a test: the command per stack (AppLaunchResolver) and the process that
// has to answer at the URL (AppLauncher).
public class AppLauncherTests : IDisposable
{
    private const string WebProject = """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>""";

    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Run_command_of_the_project_file_wins_over_the_stack()
    {
        _workspace.Write("src/Web/Web.csproj", WebProject);
        _workspace.Write(ProjectKeyResolver.OverrideFile, """{ "key": "eep", "run": "npm run dev:all" }""");

        var command = AppLaunchResolver.Resolve(_workspace.Root, ["src/Web/Web.csproj"], "http://localhost:5211", out var error);

        Assert.Null(error);
        Assert.Equal("npm run dev:all", command?.Display);
        Assert.Equal(_workspace.Root, command?.WorkingDirectory);
        Assert.Equal("5211", command?.Environment["PORT"]);
    }

    [Fact]
    public void Dotnet_runs_the_web_project_whose_launch_settings_use_the_port()
    {
        _workspace.Write("src/Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        _workspace.Write("src/Admin/Admin.csproj", WebProject);
        _workspace.Write("src/Admin/Properties/launchSettings.json", """{ "profiles": { "http": { "applicationUrl": "http://localhost:5100" } } }""");
        _workspace.Write("src/Web/Web.csproj", WebProject);
        _workspace.Write("src/Web/Properties/launchSettings.json", """{ "profiles": { "Web": { "commandName": "Project", "applicationUrl": "https://localhost:7211;http://localhost:5211" } } }""");

        var command = AppLaunchResolver.Resolve(_workspace.Root, ["src/Admin/Admin.csproj", "src/Lib/Lib.csproj", "src/Web/Web.csproj"], "http://localhost:5211/app", out var error);

        Assert.Null(error);
        Assert.Equal(".NET", command?.Stack);
        Assert.Equal("dotnet run --project src/Web/Web.csproj --launch-profile Web --urls http://localhost:5211", command?.Display);
        Assert.Equal(Path.Combine(_workspace.Root, "src", "Web"), command?.WorkingDirectory);
        Assert.Equal("Development", command?.Environment["ASPNETCORE_ENVIRONMENT"]);
    }

    [Fact]
    public void Dotnet_prefers_the_profile_listing_only_the_tested_url()
    {
        _workspace.Write("Web/Web.csproj", WebProject);
        _workspace.Write("Web/Properties/launchSettings.json", """
            {
              "profiles": {
                "https": { "commandName": "Project", "applicationUrl": "https://localhost:7121;http://localhost:5211" },
                "http": { "commandName": "Project", "applicationUrl": "http://localhost:5211" },
                "IIS Express": { "commandName": "IISExpress" },
                "Docker": { "commandName": "Docker", "applicationUrl": "http://localhost:5211" }
              }
            }
            """);

        var http = AppLaunchResolver.Resolve(_workspace.Root, ["Web/Web.csproj"], "http://localhost:5211", out _);
        var https = AppLaunchResolver.Resolve(_workspace.Root, ["Web/Web.csproj"], "https://localhost:7121", out _);

        Assert.Equal(["run", "--project", Path.Combine(_workspace.Root, "Web", "Web.csproj"), "--launch-profile", "http", "--urls", "http://localhost:5211"], http?.Arguments);
        Assert.Contains("--launch-profile https", https?.Display);
    }

    [Fact]
    public void Dotnet_without_a_profile_on_the_port_runs_the_only_web_project_without_one()
    {
        _workspace.Write("Web/Web.csproj", WebProject);
        _workspace.Write("Web/Properties/launchSettings.json", """{ "profiles": { "http": { "commandName": "Project", "applicationUrl": "http://localhost:5000" } } }""");

        var command = AppLaunchResolver.Resolve(_workspace.Root, ["Web/Web.csproj"], "http://localhost:5211", out _);

        Assert.Equal("dotnet run --project Web/Web.csproj --urls http://localhost:5211", command?.Display);
        Assert.Contains("--no-launch-profile", command!.Arguments);
    }

    [Fact]
    public void Several_web_projects_without_the_port_are_not_guessed()
    {
        _workspace.Write("src/Admin/Admin.csproj", WebProject);
        _workspace.Write("src/Web/Web.csproj", WebProject);

        Assert.Null(AppLaunchResolver.Resolve(_workspace.Root, ["src/Admin/Admin.csproj", "src/Web/Web.csproj"], "http://localhost:5211", out var error));
        Assert.Contains(".aithread/project.json", error);
    }

    [Fact]
    public void Node_runs_the_dev_script_once_its_packages_are_installed()
    {
        _workspace.Write("web/package.json", """{ "scripts": { "start": "node server.js", "dev": "vite" } }""");

        Assert.Null(AppLaunchResolver.Resolve(_workspace.Root, ["web/package.json"], "http://localhost:5173", out var missing));
        Assert.Contains("npm install", missing);

        Directory.CreateDirectory(Path.Combine(_workspace.Root, "web", "node_modules"));
        var command = AppLaunchResolver.Resolve(_workspace.Root, ["web/package.json"], "http://localhost:5173", out var error);

        Assert.Null(error);
        Assert.Equal("npm run dev (di web)", command?.Display);
        Assert.Equal(Path.Combine(_workspace.Root, "web"), command?.WorkingDirectory);
    }

    [Fact]
    public void Laravel_is_served_by_artisan_on_the_port()
    {
        _workspace.Write("composer.json", "{}");
        _workspace.Write("artisan", "<?php");

        var command = AppLaunchResolver.Resolve(_workspace.Root, ["composer.json"], "http://127.0.0.1:8000", out _);

        Assert.Equal(["artisan", "serve", "--host=127.0.0.1", "--port=8000"], command?.Arguments);
    }

    [Fact]
    public void Unknown_stack_says_how_to_add_the_run_command()
    {
        _workspace.Write("go.mod", "module demo");

        Assert.Null(AppLaunchResolver.Resolve(_workspace.Root, ["go.mod"], "http://localhost:8080", out var error));
        Assert.Equal(AppLaunchResolver.Unsupported, error);
    }

    [Fact]
    public async Task App_that_exits_before_answering_reports_its_exit_code_and_output()
    {
        var port = FreePort();
        // Twenty build warnings before the error, as dotnet run prints them; only the error may be left in the message.
        var warnings = string.Join(OperatingSystem.IsWindows() ? "& " : "; ",
            Enumerable.Range(1, 20).Select(i => $"echo D:\\app\\Lib.csproj : warning NU1903: Package {i} has a known vulnerability"));
        var (program, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", (IReadOnlyList<string>)["/d", "/c", $"{warnings}& echo Unhandled exception. System.NullReferenceException& exit 3"])
            : ("/bin/sh", ["-c", $"{warnings}; echo Unhandled exception. System.NullReferenceException; exit 3"]);
        var command = new AppLaunchCommand("custom", program, arguments, _workspace.Root, "start-app", new Dictionary<string, string>());

        var result = await new AppLauncher(NullLogger<AppLauncher>.Instance).StartAsync(command, $"http://localhost:{port}", CancellationToken.None);

        Assert.Null(result.App);
        Assert.Equal("start-app berhenti dengan exit code 3 sebelum menjawab", result.Error);
        Assert.Equal("Unhandled exception. System.NullReferenceException", result.Output?.Trim());
    }

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
