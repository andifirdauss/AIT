using AiThread.Service.Verification;

namespace AiThread.Tests;

public class BuildCommandResolverTests : IDisposable
{
    private readonly TempWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void Dotnet_prefers_the_shallowest_solution()
    {
        _workspace.Write("App.slnx", "<Solution />");
        _workspace.Write("src/Api/Api.csproj", "<Project />");
        _workspace.Write("tools/Other.sln", "");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["src/Api/Api.csproj", "tools/Other.sln", "App.slnx"], null, out var error);

        Assert.NotNull(command);
        Assert.Null(error);
        Assert.Equal(".NET", command.Stack);
        Assert.Equal("dotnet", command.FileName);
        Assert.Equal("dotnet build --no-incremental App.slnx", command.Display);
        Assert.Equal(Path.Combine(_workspace.Root, "App.slnx"), command.Arguments[1]);
    }

    [Fact]
    public void Dotnet_without_solution_builds_the_shallowest_project()
    {
        _workspace.Write("src/Web/Web.csproj", "<Project />");
        _workspace.Write("src/Web/Lib/Lib.csproj", "<Project />");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["src/Web/Lib/Lib.csproj", "src/Web/Web.csproj"], null, out _);

        Assert.Equal("dotnet build --no-incremental src/Web/Web.csproj", command?.Display);
    }

    [Fact]
    public void Target_must_be_a_manifest_inside_the_root()
    {
        _workspace.Write("App.slnx", "<Solution />");
        _workspace.Write("src/Api/Api.csproj", "<Project />");
        _workspace.Write("src/Api/Program.cs", "class P {}");

        Assert.Equal("dotnet build --no-incremental src/Api/Api.csproj", BuildCommandResolver.Resolve(_workspace.Root, [], "src/Api/Api.csproj", out _)?.Display);
        Assert.Equal("dotnet build --no-incremental src/Api/Api.csproj", BuildCommandResolver.Resolve(_workspace.Root, [], "src/Api", out _)?.Display);

        Assert.Null(BuildCommandResolver.Resolve(_workspace.Root, [], "src/Api/Program.cs", out var notManifest));
        Assert.Contains("not a solution", notManifest);

        Assert.Null(BuildCommandResolver.Resolve(_workspace.Root, [], "../elsewhere/App.slnx", out var outside));
        Assert.Contains("outside the project root", outside);

        Assert.Null(BuildCommandResolver.Resolve(_workspace.Root, [], "src/Missing.csproj", out var missing));
        Assert.Contains("does not exist", missing);
    }

    [Fact]
    public void Node_prefers_a_local_typescript_check_over_the_build_script()
    {
        _workspace.Write("package.json", """{ "scripts": { "build": "vite build" } }""");
        _workspace.Write("tsconfig.json", "{}");
        _workspace.Write("node_modules/typescript/bin/tsc", "// tsc");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["package.json"], null, out _);

        Assert.NotNull(command);
        Assert.Equal("node", command.FileName);
        Assert.Equal("tsc --noEmit", command.Display);
        Assert.Contains("--noEmit", command.Arguments);
    }

    [Fact]
    public void Node_falls_back_to_the_build_script()
    {
        _workspace.Write("web/package.json", """{ "scripts": { "build": "webpack" } }""");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["web/package.json"], null, out _);

        Assert.NotNull(command);
        Assert.Equal("npm run build (in web)", command.Display);
        Assert.Equal(Path.Combine(_workspace.Root, "web"), command.WorkingDirectory);
        Assert.Contains("build", command.Arguments);
    }

    [Fact]
    public void Projects_without_a_build_step_have_no_command()
    {
        _workspace.Write("package.json", """{ "scripts": { "start": "node index.js" } }""");
        _workspace.Write("requirements.txt", "flask");

        Assert.Null(BuildCommandResolver.Resolve(_workspace.Root, ["package.json", "requirements.txt"], null, out var error));
        Assert.Equal(BuildCommandResolver.Unsupported, error);
    }

    [Fact]
    public void Dotnet_wins_over_other_stacks_in_the_same_repository()
    {
        _workspace.Write("package.json", """{ "scripts": { "build": "gulp" } }""");
        _workspace.Write("src/App/App.csproj", "<Project />");

        Assert.Equal(".NET", BuildCommandResolver.Resolve(_workspace.Root, ["package.json", "src/App/App.csproj"], null, out _)?.Stack);
    }

    private const string LegacyWebProject = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <VSToolsPath Condition="'$(VSToolsPath)' == ''">$(MSBuildExtensionsPath32)\Microsoft\VisualStudio\v$(VisualStudioVersion)</VSToolsPath>
          </PropertyGroup>
          <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
          <Import Project="$(VSToolsPath)\WebApplications\Microsoft.WebApplication.targets" Condition="'$(VSToolsPath)' != ''" />
        </Project>
        """;

    private const string FakeMsBuild = @"C:\VS\MSBuild\Current\Bin\MSBuild.exe";

    [Fact]
    public void Old_format_project_rebuilds_with_visual_studio_msbuild()
    {
        _workspace.Write("Web/Web.csproj", LegacyWebProject);

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["Web/Web.csproj"], null, () => FakeMsBuild, out var error);

        Assert.NotNull(command);
        Assert.Null(error);
        Assert.Equal(".NET Framework", command.Stack);
        Assert.Equal(FakeMsBuild, command.FileName);
        Assert.Equal(Path.Combine(_workspace.Root, "Web", "Web.csproj"), command.Arguments[0]);
        Assert.Contains("-t:Rebuild", command.Arguments);
        Assert.Contains("-restore", command.Arguments);
        Assert.Equal("msbuild -t:Rebuild Web/Web.csproj", command.Display);
        Assert.Equal([Path.Combine(_workspace.Root, "Web", "bin")], command.OutputDirectories);
        Assert.Contains("-t:Build", command.Incremental!.Arguments);
        Assert.DoesNotContain("-t:Rebuild", command.Incremental.Arguments);
    }

    [Fact]
    public void Solution_with_an_old_format_project_builds_with_msbuild()
    {
        _workspace.Write("App.sln", """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Web", "Web\Web.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "Lib", "Lib\Lib.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            """);
        _workspace.Write("Web/Web.csproj", LegacyWebProject);
        _workspace.Write("Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["App.sln", "Web/Web.csproj", "Lib/Lib.csproj"], null, () => FakeMsBuild, out _);

        Assert.Equal("msbuild -t:Rebuild App.sln", command?.Display);
    }

    [Fact]
    public void Sdk_projects_never_ask_for_msbuild()
    {
        _workspace.Write("App.slnx", """<Solution><Project Path="Lib/Lib.csproj" /></Solution>""");
        _workspace.Write("Lib/Lib.csproj", """
            <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
            </Project>
            """);

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["App.slnx", "Lib/Lib.csproj"], null, () => throw new InvalidOperationException("not needed"), out _);

        Assert.Equal("dotnet", command?.FileName);
        Assert.Contains("--no-incremental", command!.Arguments);
        Assert.Equal("dotnet build App.slnx", command.Incremental?.Display);
        Assert.DoesNotContain("--no-incremental", command.Incremental!.Arguments);
        Assert.Equal([Path.Combine(_workspace.Root, "Lib", "bin")], command.OutputDirectories);
    }

    [Fact]
    public void Old_format_project_without_msbuild_is_not_built_by_a_side_stack()
    {
        _workspace.Write("package.json", """{ "scripts": { "build": "gulp" } }""");
        _workspace.Write("Web/Web.csproj", LegacyWebProject);

        Assert.Null(BuildCommandResolver.Resolve(_workspace.Root, ["package.json", "Web/Web.csproj"], null, () => null, out var error));
        Assert.Equal(BuildCommandResolver.NoMsBuild, error);
    }
}
