using AiThread.Service.Verification;

namespace AiThread.Tests;

public class BuildOutputParserTests
{
    private static readonly string WorkingDirectory = Path.Combine(Path.GetTempPath(), "aithread-build");

    [Fact]
    public void Reads_roslyn_errors_and_drops_the_project_suffix()
    {
        var parser = new BuildOutputParser(WorkingDirectory);
        parser.Add(@"C:\src\App\Services\UserService.cs(12,17): error CS0246: The type or namespace name 'Foo' could not be found (are you missing a using directive or an assembly reference?) [C:\src\App\App.csproj]");

        var error = Assert.Single(parser.Errors);
        Assert.Equal(@"C:\src\App\Services\UserService.cs", error.File);
        Assert.Equal(12, error.Line);
        Assert.Equal(17, error.Column);
        Assert.Equal("CS0246", error.Code);
        Assert.EndsWith("assembly reference?)", error.Message);
    }

    [Fact]
    public void Deduplicates_repeated_errors_per_target_framework_and_summary()
    {
        var parser = new BuildOutputParser(WorkingDirectory);
        parser.Add(@"C:\src\A.cs(3,5): error CS1002: ; expected [C:\src\A.csproj::TargetFramework=net8.0]");
        parser.Add(@"C:\src\A.cs(3,5): error CS1002: ; expected [C:\src\A.csproj::TargetFramework=net10.0]");
        parser.Add(@"C:\src\A.cs(3,5): error CS1002: ; expected [C:\src\App.slnx]");

        Assert.Single(parser.Errors);
    }

    [Fact]
    public void Counts_warnings_without_keeping_them()
    {
        var parser = new BuildOutputParser(WorkingDirectory);
        parser.Add(@"C:\src\A.cs(1,1): warning CS8618: Non-nullable property 'Name' must contain a non-null value.");
        parser.Add(@"C:\src\App.csproj : warning NU1900: Error occurred while getting package vulnerability data.");

        Assert.Empty(parser.Errors);
        Assert.Equal(2, parser.WarningCount);
    }

    [Theory]
    [InlineData(@"C:\src\App\App.csproj : error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org", @"C:\src\App\App.csproj", "NU1101")]
    [InlineData("MSBUILD : error MSB1009: Project file does not exist.", null, "MSB1009")]
    [InlineData("CSC : error CS2001: Source file 'x.cs' could not be found.", null, "CS2001")]
    public void Reads_errors_without_a_line(string line, string? file, string code)
    {
        var error = new BuildOutputParser(WorkingDirectory).Parse(line);

        Assert.NotNull(error);
        Assert.True(error.IsError);
        Assert.Equal(file, error.File);
        Assert.Null(error.Line);
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void Locked_output_copies_are_not_code_errors()
    {
        var error = new BuildOutputParser(WorkingDirectory).Parse(
            @"C:\Program Files\dotnet\sdk\10.0.100\Microsoft.Common.CurrentVersion.targets(4970,5): error MSB3027: Could not copy ""obj\Debug\App.dll"" to ""bin\Debug\App.dll"". Exceeded retry count of 1. The file is locked by: ""App (1234)"" [C:\src\App\App.csproj]");

        Assert.NotNull(error);
        Assert.True(error.IsLockedOutput);
        var result = new BuildResult("dotnet build", false, false, 1, [error], 0, string.Empty, 10);
        Assert.True(result.OnlyLockedOutput);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Reads_typescript_errors_relative_to_the_working_directory()
    {
        var error = new BuildOutputParser(WorkingDirectory).Parse("src/app.ts(3,5): error TS2304: Cannot find name 'x'.");

        Assert.NotNull(error);
        Assert.Equal(Path.Combine(WorkingDirectory, "src", "app.ts"), error.File);
        Assert.Equal("TS2304", error.Code);
        Assert.Equal(3, error.Line);
    }

    [Fact]
    public void Reads_go_rust_and_maven_formats()
    {
        var parser = new BuildOutputParser(WorkingDirectory);

        var go = parser.Parse("./internal/user.go:14:2: undefined: repo");
        Assert.NotNull(go);
        Assert.Equal(Path.Combine(WorkingDirectory, "internal", "user.go"), go.File);
        Assert.Equal((14, 2), (go.Line, go.Column));
        Assert.Equal("undefined: repo", go.Message);

        var rust = parser.Parse("src/main.rs:3:5: error[E0425]: cannot find value `x` in this scope");
        Assert.NotNull(rust);
        Assert.Equal("E0425", rust.Code);
        Assert.Equal("cannot find value `x` in this scope", rust.Message);

        var rustWarning = parser.Parse("src/lib.rs:8:9: warning: unused variable: `y`");
        Assert.NotNull(rustWarning);
        Assert.False(rustWarning.IsError);

        var maven = parser.Parse("[ERROR] /C:/src/app/src/main/java/App.java:[10,5] cannot find symbol");
        Assert.NotNull(maven);
        Assert.Equal(@"C:\src\app\src\main\java\App.java", maven.File, ignoreCase: true);
        Assert.Equal(10, maven.Line);
    }

    [Theory]
    [InlineData("Build succeeded.")]
    [InlineData("Time Elapsed 00:00:46.61")]
    [InlineData("  Restore complete (0.6s)")]
    [InlineData("    0 Error(s)")]
    [InlineData("See https://example.com:8080/docs for details")]
    public void Ignores_lines_that_are_not_diagnostics(string line)
    {
        Assert.Null(new BuildOutputParser(WorkingDirectory).Parse(line));
    }
}
