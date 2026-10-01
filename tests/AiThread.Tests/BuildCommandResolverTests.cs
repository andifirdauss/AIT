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
        Assert.Equal("dotnet build App.slnx", command.Display);
        Assert.Equal(Path.Combine(_workspace.Root, "App.slnx"), command.Arguments[1]);
    }

    [Fact]
    public void Dotnet_without_solution_builds_the_shallowest_project()
    {
        _workspace.Write("src/Web/Web.csproj", "<Project />");
        _workspace.Write("src/Web/Lib/Lib.csproj", "<Project />");

        var command = BuildCommandResolver.Resolve(_workspace.Root, ["src/Web/Lib/Lib.csproj", "src/Web/Web.csproj"], null, out _);

        Assert.Equal("dotnet build src/Web/Web.csproj", command?.Display);
    }

    [Fact]
    public void Target_must_be_a_manifest_inside_the_root()
    {
        _workspace.Write("App.slnx", "<Solution />");
        _workspace.Write("src/Api/Api.csproj", "<Project />");
        _workspace.Write("src/Api/Program.cs", "class P {}");

        Assert.Equal("dotnet build src/Api/Api.csproj", BuildCommandResolver.Resolve(_workspace.Root, [], "src/Api/Api.csproj", out _)?.Display);
        Assert.Equal("dotnet build src/Api/Api.csproj", BuildCommandResolver.Resolve(_workspace.Root, [], "src/Api", out _)?.Display);

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
}
