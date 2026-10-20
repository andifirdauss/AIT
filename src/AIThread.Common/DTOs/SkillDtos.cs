using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// Model = null means the model of the first active Planner node.
public record SkillSettingsDto(string? Model, int MaxPromptChars, string StoreDirectory);

public record UpdateSkillSettingsRequest(string? Model, int MaxPromptChars = 40_000);

public record SkillSourceDto(
    Guid Id,
    string Url,
    string Name,
    SkillSourceKind Kind,
    string? Ref,
    string? SubPath,
    SkillSourceStatus Status,
    string? Error,
    int SkillCount,
    DateTime? LastPulledAt,
    DateTime CreatedAt,
    string? Progress = null);

public record AddSkillSourceRequest(string Url);

public record SkillDto(
    Guid Id,
    Guid SourceId,
    string SourceName,
    string Name,
    string Description,
    string? Tags,
    string RelativeDir,
    int NodeCount,
    string? Snippet = null);

public record SkillDetailDto(
    Guid Id,
    Guid SourceId,
    string SourceName,
    string SourceUrl,
    string Name,
    string Description,
    string? Tags,
    string Folder,
    string EntryFile,
    string Content,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> NodeNames);

// Role = the role of the node being edited, so the orchestrator can prefer skills that fit it.
public record SuggestSkillsRequest(string Prompt, NodeRole? Role = null);

public record SkillSuggestionDto(
    Guid Id,
    string Name,
    string Description,
    string SourceName,
    string Reason,
    double Score,
    bool Recommended);

public record NodeSkillDto(Guid Id, string Name, string Description, string SourceName);
