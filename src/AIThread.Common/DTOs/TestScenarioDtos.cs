namespace AIThread.Common.DTOs;

// One step of a scenario: who (a role of the scenario's accounts), what (submit, approve, ...), where, how (the
// instruction and the form values the developer chose) and what must be seen afterwards.
public record ScenarioStepDto(
    int Order,
    string? Role,
    string Action,
    string? Target,
    string Instruction,
    IReadOnlyDictionary<string, string> Values,
    string Expect);

// A role the scenario needs and the test account that plays it (null until the developer picks one).
public record ScenarioRoleDto(string Role, string? Username);

// Missing: what the sheet still needs before a run (a role without an account), in the UI language.
public record TestingScenarioDto(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    int Version,
    string Level,
    string? BaseUrl,
    IReadOnlyList<ScenarioStepDto> Steps,
    IReadOnlyList<ScenarioRoleDto> Roles,
    IReadOnlyList<string> Fields,
    Guid? SourceThreadId,
    int? SourceFollowUp,
    IReadOnlyList<string> Missing,
    string? LastOutcome,
    DateTime? LastRunAt,
    string? LastRunBy,
    string? CreatedBy,
    string? UpdatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

// AccountsAdded: usernames of the sheet's Akun tab that became test accounts of the project.
public record ScenarioImportResultDto(TestingScenarioDto Scenario, IReadOnlyList<string> AccountsAdded, IReadOnlyList<string> Warnings);

// Acknowledge: the developer read that the run creates real data and may send notifications (asked once per project).
public record RunScenarioRequest(bool Acknowledge);
