using AIThread.Common.Enums;

namespace AIThread.Common.DTOs;

// A standing permission ("allow always") of a project on this machine, for Project → Detail → AIT permissions.
public record ApprovalGrantDto(Guid Id, ApprovalKind Kind, string Target, string? CreatedBy, DateTime CreatedAt, DateTime? LastUsedAt, int UseCount);

public record ApprovalItemDto(ApprovalKind Kind, string Target, string? Detail);

// What a thread waits for the developer to allow, for the buttons on the thread page. Text is the card the CLI got.
public record PendingApprovalDto(IReadOnlyList<ApprovalItemDto> Items, string Reason, bool AllowAlways, string Text, bool AfterThread);

public record ApproveRequest(ApprovalDecision Decision);
