using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// One step of the first-run setup (menu Setup). Done is computed from the live state each time, never stored.
// Required = AIT cannot be used without it; the other steps are recommended.
public record SetupStepDto(string Id, string Title, bool Required, bool Done, string Detail);

public record SetupStatusDto(IReadOnlyList<SetupStepDto> Steps, int RequiredCount, int RequiredDone, bool IsComplete);

// Tests the values first and saves them only when 9router answers and accepts the key. ApiKey empty = keep the
// stored key.
public record SetupRouterRequest(string BaseUrl, string? ApiKey);

public record SetupRouterResultDto(RouterStatusDto Status, bool Saved, string Message);

// One click pipeline: the strong model plans (and reviews), the cheap one does the rest. CheapModel empty = the strong
// model everywhere. Running it again updates the same nodes instead of adding new ones.
public record SetupPresetRequest(string StrongModel, string? CheapModel, bool ToolsNode = true, bool Reviewer = false, bool ContextKeeper = true);

// Created = a new node; otherwise an existing preset node was updated (or deactivated when its role was unticked).
public record SetupPresetNodeDto(NodeDto Node, bool Created);

public record SetupPresetResultDto(IReadOnlyList<SetupPresetNodeDto> Nodes);

// A team profile: what a team lead sets up once and every developer imports, so a new machine only needs its own
// secrets. Secrets (9router API key, proxy API key, database passwords, Telegram) and personal values (developer name)
// are never written to it.
public record SetupProfileFile(
    string Format,
    int Version,
    DateTime ExportedAt,
    string? AppVersion,
    SetupProfileRouter? Router,
    SetupProfileOrchestration? Orchestration,
    IReadOnlyList<SetupProfileNode>? Nodes,
    SetupProfileMemory? Memory,
    SetupProfileBusiness? Business,
    SetupProfileSkills? Skills)
{
    public const string FormatName = "aithread-setup";
    public const int CurrentVersion = 1;
}

public record SetupProfileRouter(string BaseUrl);

public record SetupProfileOrchestration(
    bool OrchestrationEnabled,
    bool StreamProgressToClient,
    bool PlannerCanAsk,
    string? PassthroughModel,
    int PlannerMaxSteps,
    int ExecutorMaxRetries,
    int MaxParallelTasks,
    int RequestTimeoutSeconds,
    bool BuildEnabled,
    int BuildTimeoutSeconds,
    int MaxFixRounds);

// Nodes refer to each other by name: Fallback and ToolsNode are node names in the same file.
public record SetupProfileNode(
    string Name,
    NodeRole Role,
    string Model,
    double? Temperature,
    int MaxTokens,
    int MaxConcurrency,
    int Priority,
    string? SystemPrompt,
    bool IsActive,
    string? Fallback = null,
    IReadOnlyList<SetupProfileDelegation>? ToolDelegations = null,
    IReadOnlyList<SetupProfileSkillRef>? Skills = null);

public record SetupProfileDelegation(string Tool, string ToolsNode);

// A skill of the store, by name and the link of its source (Skills.Sources holds the links to pull).
public record SetupProfileSkillRef(string Name, string? SourceUrl);

public record SetupProfileMemory(SetupProfileStore Short, SetupProfileStore Long, SetupProfileStore Business);

public record SetupProfileStore(MemoryStoreProvider Provider, string? Host, int? Port, string? Database, string? Username, string? Options);

public record SetupProfileBusiness(bool Enabled, int IntervalHours, string? Model);

public record SetupProfileSkills(string? Model, int MaxPromptChars, IReadOnlyList<string> Sources);

public enum SetupProfileSection
{
    Router = 1,
    Orchestration = 2,
    Nodes = 3,
    Memory = 4,
    Business = 5,
    Skills = 6
}

public enum SetupProfileNodeMode
{
    // Nodes of the file are added or updated (matched by name and role); other nodes stay as they are.
    Merge = 1,

    // As Merge, and the other active nodes are deactivated (not deleted), so the pipeline is exactly the file's.
    Replace = 2
}

// Secrets typed on this machine; empty = keep what is stored.
public record SetupProfileSecrets(string? NineRouterApiKey, string? ShortPassword, string? LongPassword, string? BusinessPassword);

// Sections null = every section in the file. DryRun only reports what would change.
public record SetupProfileImportRequest(
    SetupProfileFile Profile,
    IReadOnlyList<SetupProfileSection>? Sections = null,
    SetupProfileNodeMode NodeMode = SetupProfileNodeMode.Merge,
    SetupProfileSecrets? Secrets = null,
    bool CopyExistingMemory = true,
    bool DryRun = true);

public record SetupProfileChangeDto(SetupProfileSection Section, string Text);

// A secret the profile may need on this machine. Stored = one is already saved for the same target.
public record SetupProfileSecretDto(string Id, string Label, bool Stored);

// Sections: the sections the file holds. Errors: sections that could not be applied (the others were).
public record SetupProfileImportResultDto(
    bool DryRun,
    IReadOnlyList<SetupProfileSection> Sections,
    IReadOnlyList<SetupProfileChangeDto> Changes,
    IReadOnlyList<SetupProfileSecretDto> Secrets,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
