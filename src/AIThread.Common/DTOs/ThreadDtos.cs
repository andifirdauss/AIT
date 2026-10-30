using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

public record ThreadSummaryDto(
    Guid Id,
    Guid? ProjectId,
    string? ProjectName,
    ThreadSource Source,
    string? SessionId,
    string Prompt,
    ThreadStatus Status,
    string? PlanSummary,
    int TaskCount,
    int TasksSucceeded,
    int TasksFailed,
    long? TotalMs,
    long? ExecutionWallMs,
    long? ExecutionSumMs,
    int TotalTokens,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    GitChangeState? GitState = null);

public record ThreadTaskDto(
    Guid Id,
    string TaskKey,
    int Order,
    string Title,
    string FilePath,
    string Action,
    string? TargetPath,
    string Instruction,
    string? DependsOn,
    ThreadTaskStatus Status,
    string? NodeName,
    string? Model,
    int Attempts,
    string? Summary,
    string? Diff,
    string? Error,
    int Tokens,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    long? DurationMs);

public record ThreadDetailDto(
    ThreadSummaryDto Summary,
    string? PlanJson,
    string? FinalResponse,
    string? Error,
    string? PlannerModel,
    string? WrapperModel,
    int PlannerSteps,
    int PlannerTokens,
    int ExecutorTokens,
    int WrapperTokens,
    long? PlanningMs,
    long? WrappingMs,
    string? WorkingDirectory,
    IReadOnlyList<ThreadTaskDto> Tasks,
    string? ContextPack = null,
    string? ReviewerModel = null,
    int ReviewerTokens = 0,
    long? VerificationMs = null,
    string? VerificationJson = null,
    int PlannerCachedTokens = 0,
    int RetryCount = 0,
    DateTime? RolledBackAt = null,
    int ToolsNodeTokens = 0,
    string? TesterModel = null,
    int TesterTokens = 0,
    long? TestingMs = null,
    string? TestingJson = null,
    long? UnitTestMs = null,
    string? UnitTestJson = null,
    ThreadGitDto? Git = null);

// An image of a prompt started outside a coding CLI (aithread chat). Data: base64 without a "data:" prefix.
public record ImageAttachmentDto(string MediaType, string Data);

// Images: in the order of the [Image #N] markers of the prompt (Image N of the planner); limits in PromptImages.
// SessionId: the aithread chat session the prompt belongs to (ChatSessionKey); null for a prompt on its own (dashboard).
public record StartThreadRequest(Guid ProjectId, string Prompt, IReadOnlyList<ImageAttachmentDto>? Images = null, string? SessionId = null);

// One aithread chat session of a project, for resuming it: Title is its first prompt, LastPrompt its latest.
public record ChatSessionDto(string SessionId, string Title, string LastPrompt, int Turns, DateTime StartedAt, DateTime LastAt, ThreadStatus LastStatus);

// The login for one retest from the thread page; both empty = without login. Never stored.
public record RetestRequest(string? Username, string? Password);

// Action per file when undoing a thread: "restore" (put the original content back), "delete" (the thread created the
// file), "unchanged" (nothing to undo) or "conflict" (changed again after the thread; Problem says how).
public record RollbackFileDto(string FilePath, string Action, string? Problem);

// CanRollback is false when Reason is set or any file is a conflict: undoing is all or nothing.
public record RollbackPreviewDto(bool CanRollback, string? Reason, IReadOnlyList<RollbackFileDto> Files);

public record RollbackResultDto(int Restored, int Deleted, IReadOnlyList<RollbackFileDto> Files);
