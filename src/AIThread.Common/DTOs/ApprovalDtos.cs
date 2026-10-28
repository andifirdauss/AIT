using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// A standing permission ("allow always") of a project on this machine, for Project → Detail → AIT permissions.
public record ApprovalGrantDto(Guid Id, ApprovalKind Kind, string Target, string? CreatedBy, DateTime CreatedAt, DateTime? LastUsedAt, int UseCount);

// Pattern: the command pattern "allow always for the pattern" would save ("npm install *"); null when none is offered.
public record ApprovalItemDto(ApprovalKind Kind, string Target, string? Detail, string? Pattern = null);

// What a thread waits for the developer to allow, for the buttons on the thread page. Text is the card the CLI got.
// AllowPattern: the "always allow the pattern" option is offered (an item has a Pattern). AllowAll: "always allow every
// command of the project" is offered (an item is a command).
public record PendingApprovalDto(IReadOnlyList<ApprovalItemDto> Items, string Reason, bool AllowAlways, string Text, bool AfterThread, bool AllowPattern = false, bool AllowAll = false);

public record ApproveRequest(ApprovalDecision Decision);

// Project → Detail → AIT permissions: a standing permission added by hand (a command, or a pattern "npm install *").
public record AddApprovalRequest(ApprovalKind Kind, string Target);
