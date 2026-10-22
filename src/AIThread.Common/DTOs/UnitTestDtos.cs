using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// A test of the catalog as Project > Detail shows it. The source snapshot is internal and never part of a DTO.
public record UnitTestCaseDto(
    Guid Id,
    int Number,
    string DisplayNumber,
    string Title,
    string? Purpose,
    string? Scenario,
    string? Module,
    IReadOnlyList<string> Layers,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> TargetFiles,
    string Framework,
    string? TestProject,
    string TestFile,
    string? ClassName,
    string MethodName,
    string Selector,
    IReadOnlyList<string> Requires,
    UnitTestState State,
    UnitTestOrigin Origin,
    string? CreatedBy,
    Guid? CreatedThreadId,
    DateTime CreatedAt,
    string? ObsoleteReason,
    Guid? ObsoleteThreadId,
    string? ObsoleteBy,
    DateTime? ObsoleteAt,
    string? QuarantineReason,
    UnitTestResult LastResult,
    DateTime? LastRunAt,
    string? LastRunBy,
    long? LastDurationMs,
    string? LastMessage,
    string? LastSkipReason,
    bool HasSnapshot);

public record UnitTestSettingsDto(UnitTestMode Mode, UnitTestAuthoring Authoring, bool KeepSnapshots, int? TimeoutSeconds, string? UpdatedBy, DateTime? UpdatedAt);

public record SaveUnitTestSettingsRequest(UnitTestMode Mode, UnitTestAuthoring Authoring, bool KeepSnapshots, int? TimeoutSeconds);

// A group of tests AIT can run: a .NET test project, a Node package with jest/vitest/mocha, a pytest root, ...
public record UnitTestSuiteDto(string Framework, string Stack, string Manifest, bool PerTestResults, string? Problem);

// TesterAvailable: an active Tester node triages failing tests; without it a regression is fixed in the code directly.
public record UnitTestCatalogDto(
    string ProjectKey,
    UnitTestSettingsDto Settings,
    IReadOnlyList<UnitTestSuiteDto> Suites,
    IReadOnlyList<UnitTestCaseDto> Tests,
    bool TesterAvailable);

public record UnitTestScanResultDto(int Found, int Added, int Updated, int Moved, int Missing, int Labeled, IReadOnlyList<string> Warnings);

// Ids empty and All false: nothing. All: every active test.
public record RunUnitTestsRequest(IReadOnlyList<Guid>? Ids, bool All);

public record UnitTestRunItemDto(Guid Id, string DisplayNumber, string Title, UnitTestResult Result, long? DurationMs, string? Message, string? SkipReason);

public record UnitTestRunResultDto(int Ran, int Passed, int Failed, int Skipped, long DurationMs, IReadOnlyList<UnitTestRunItemDto> Tests, IReadOnlyList<string> Warnings);

public record UnitTestHistoryDto(Guid Id, string Phase, UnitTestResult Result, long? DurationMs, string? Message, string? SkipReason, string? Machine,
    string? RunBy, Guid? ThreadId, DateTime RunAt);

// Removing an obsolete test: the catalog marks it Removed and, when RemoveCode is set, a thread deletes its code.
public record ConfirmUnitTestRemovalRequest(bool RemoveCode = true);

public record ConfirmUnitTestRemovalResultDto(UnitTestCaseDto Test, Guid? ThreadId);
