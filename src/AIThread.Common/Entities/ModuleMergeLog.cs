using AIThread.Common.Enums;

namespace AIThread.Common.Entities;

// One merge of two modules of a project: applied (and undoable), or suggested and waiting for review.
public class ModuleMergeLog : BaseEntity
{
    public string ProjectKey { get; set; } = string.Empty;
    public string FromModule { get; set; } = string.Empty;
    public string IntoModule { get; set; } = string.Empty;
    public ModuleMergeStatus Status { get; set; }
    public string? Reason { get; set; }
    public double? Confidence { get; set; }

    // "Auto" or the developer's name.
    public string? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }

    // JSON of the rows the merge rewrote (ModuleMergeMoves), so an undo restores exactly those.
    public string? Moved { get; set; }
}
