namespace AIThread.Common.DTOs;

// A test account as Project > Detail → Akun uji shows it. Attributes are free key–value pairs (NIK, Unit, Atasan, ...).
public record TestAccountDto(
    Guid Id,
    string Username,
    string? Role,
    IReadOnlyDictionary<string, string> Attributes,
    string? Notes,
    string? CreatedBy,
    string? UpdatedBy,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record SaveTestAccountRequest(string Username, string? Role, Dictionary<string, string>? Attributes, string? Notes);

// What AIT found out about "login as" (an admin entering the app as another user) in a project. Status: available,
// none or unknown. How and Back are instructions for the tester (menu path, URL or endpoint); Roles are the account roles
// that may use it. Source: scan (code + cheap model), tester (seen while testing) or developer (corrected by hand).
public record ImpersonationInfo(
    string Status,
    string? How,
    string? Back,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Evidence,
    string Source,
    bool Confirmed,
    DateTime DetectedAt,
    string? DetectedBy)
{
    public const string Available = "available";
    public const string None = "none";
    public const string Unknown = "unknown";

    public const string FromScan = "scan";
    public const string FromTester = "tester";
    public const string FromDeveloper = "developer";

    public bool IsAvailable => Status == Available && !string.IsNullOrWhiteSpace(How);
}

// DevPassword is the one the tester uses: the project's, or the default while DevPasswordIsDefault.
public record TestingSettingsDto(
    string DevPassword,
    bool DevPasswordIsDefault,
    string? DevPasswordSource,
    ImpersonationInfo? Impersonation,
    string? SideEffectsAcknowledgedBy,
    DateTime? SideEffectsAcknowledgedAt,
    string? UpdatedBy,
    DateTime? UpdatedAt);

// DevPassword empty: back to the default.
public record SaveTestingSettingsRequest(string? DevPassword);

// The developer's correction of the "login as" detection.
public record SaveImpersonationRequest(string Status, string? How, string? Back, List<string>? Roles, bool Confirmed);

// CanSave is false without a project key (Problem says so). AttributeKeys: the keys the project's accounts use, as
// suggestions for the next account.
public record TestAccountsDto(
    string? ProjectKey,
    bool CanSave,
    string? Problem,
    IReadOnlyList<TestAccountDto> Accounts,
    TestingSettingsDto Settings,
    IReadOnlyList<string> AttributeKeys);
