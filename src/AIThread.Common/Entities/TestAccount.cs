namespace AIThread.Common.Entities;

// A test account of a project, shared by every developer of it through ProjectKey (kept next to long memory, like the
// unit test catalog): what the tester types to log in, the role it plays and free attributes (NIK, unit, approval level,
// its superior) the planner and the tester choose accounts by. No password: development setups accept the project's
// development password (TestingSettings).
public class TestAccount : BaseEntity
{
    public const int MaxUsernameLength = 200;
    public const int MaxRoleLength = 100;
    public const int MaxAttributesLength = 4000;
    public const int MaxNotesLength = 1000;

    public string ProjectKey { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    // Pengaju, Atasan, HR, Administrator, ... as the project calls it.
    public string? Role { get; set; }

    // JSON object of string values; the keys differ per project.
    public string? Attributes { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
