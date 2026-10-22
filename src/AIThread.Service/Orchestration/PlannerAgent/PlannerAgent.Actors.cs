namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The usernames and roles a test scenario's actor may name; null without test accounts (any actor text is kept).
    private static IReadOnlyCollection<string>? Actors(PlannerInput input) =>
        input.TestAccounts is { Accounts.Count: > 0 } set
            ? set.Accounts.Select(a => a.Username).Concat(Testing.TestAccountText.Roles(set)).ToList()
            : null;
}
