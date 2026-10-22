namespace AIThread.Common.Entities;

// How the tester logs in to a project's app in development, one row per ProjectKey. DevPassword is not a secret: it is
// the password a development setup accepts for every account (often anything), visible to every developer of the
// project. A real password never goes here; it stays in the chat login, in memory only.
public class TestingSettings : BaseEntity
{
    // Used while the project has none: long enough, with upper case, a digit and a symbol, so the usual browser-side
    // password rules let it through.
    public const string DefaultDevPassword = "Test@1234";

    public const int MaxDevPasswordLength = 200;
    public const int MaxImpersonationLength = 8000;

    public const string SourceDefault = "default";
    public const string SourceDeveloper = "developer";
    public const string SourceChat = "chat";

    public string ProjectKey { get; set; } = string.Empty;

    // Null: DefaultDevPassword.
    public string? DevPassword { get; set; }

    // default | developer (typed on Project > Detail) | chat (the answer to the tester's question).
    public string? DevPasswordSource { get; set; }

    // JSON of what AIT found out about "login as" in the project (ImpersonationInfo); null: not looked for yet.
    public string? Impersonation { get; set; }

    // Who read the warning that scenarios create real data and may send notifications (phase 3 scenarios).
    public string? SideEffectsAcknowledgedBy { get; set; }
    public DateTime? SideEffectsAcknowledgedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public string EffectiveDevPassword => string.IsNullOrEmpty(DevPassword) ? DefaultDevPassword : DevPassword;
}
