using AIThread.Common.Enums;

namespace AIThread.Service.Verification;

// How a runner reports its results: a report file AIT reads afterwards (TRX, JUnit XML, Jest-style JSON, Mocha JSON),
// its standard output (go test -json, cargo test), or nothing but the exit code.
public enum TestReportFormat
{
    Trx,
    JUnitXml,
    JestJson,
    MochaJson,
    GoJson,
    RustText,
    ExitCode
}

// A group of tests AIT can run with one command: a .NET test project, a Node package with jest/vitest/mocha, a pytest
// root, a PHPUnit config, a Maven or Gradle module, a Go module, a Cargo package. Manifest is relative to the root
// (forward slashes); Directory is where the command runs. Problem says why it cannot run on this machine (no local
// jest, no vendor/bin/phpunit, no built test assembly); such a suite is listed but never run.
public sealed record TestSuite(
    string Framework,
    string Stack,
    string Manifest,
    string Directory,
    string Root,
    TestReportFormat Format,
    string? Problem = null)
{
    public const string XUnit = "xunit";
    public const string NUnit = "nunit";
    public const string MsTest = "mstest";
    public const string DotNet = "dotnet";
    public const string Jest = "jest";
    public const string Vitest = "vitest";
    public const string Mocha = "mocha";
    public const string Npm = "npm";
    public const string Pytest = "pytest";
    public const string PhpUnit = "phpunit";
    public const string JUnit = "junit";
    public const string Go = "go";
    public const string Rust = "rust";

    public bool PerTestResults => Format != TestReportFormat.ExitCode;

    public bool CanRun => Problem is null;

    // Legacy .NET Framework projects run through vstest.console on the assembly the verification build produced.
    public bool IsLegacyDotNet { get; init; }

    // An SDK-style test project that references old-format projects (packages.config, Visual Studio targets): dotnet
    // cannot build those, so AIT builds the test project with Visual Studio's MSBuild into a folder of its own and runs
    // vstest.console on the result (TestCommand.Prepare).
    public bool BuildsWithMsBuild { get; init; }

    public static bool IsDotNet(string framework) => framework is XUnit or NUnit or MsTest or DotNet;

    public static bool IsNode(string framework) => framework is Jest or Vitest or Mocha or Npm;
}

// One catalog test as a runner needs it.
public sealed record TestTarget(string Selector, string TestFile, string? ClassName, string MethodName);

// A step that runs before the tests, in the suite's folder: the MSBuild build of a test project dotnet cannot build.
public sealed record TestPrepare(string FileName, IReadOnlyList<string> Arguments, string Display);

// A run AIT may start. Like BuildCommand, the program and the arguments are fixed per framework; a model only picks
// which catalog tests run, and those become a filter AIT builds itself. ReportPath: the report file, or the folder the
// runner writes its report files into (only files written during the run are read). Prepare: runs first; when it fails
// the tests do not run.
public sealed record TestCommand(
    TestSuite Suite,
    string FileName,
    IReadOnlyList<string> Arguments,
    string Display,
    string? ReportPath,
    bool Filtered,
    TestPrepare? Prepare = null);

// One test as a runner reported it. Name is the test method or title without data-row parameters where the format
// separates them; File is set when the format names it. Name "*" stands for every test of File or ClassName: the
// file or package failed before any test ran (compile error, import error).
public sealed record TestCaseOutcome(
    string? ClassName,
    string Name,
    string? File,
    UnitTestResult Result,
    long DurationMs,
    string? Message,
    string? SkipReason = null)
{
    public const string Everything = "*";
}

// Outcomes is empty when the runner gave no per-test results (only an exit code, or no readable report): ReportError
// says why. StartError: the program could not be started at all.
public sealed record TestRunResult(
    TestCommand Command,
    bool TimedOut,
    int? ExitCode,
    IReadOnlyList<TestCaseOutcome> Outcomes,
    string OutputTail,
    long DurationMs,
    string? StartError = null,
    string? ReportError = null)
{
    public bool Succeeded => !TimedOut && StartError is null && ExitCode == 0;
}
