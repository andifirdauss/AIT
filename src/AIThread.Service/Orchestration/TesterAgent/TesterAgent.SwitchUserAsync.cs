using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Makes a test account current: the open browser window closes (the next browser tool opens the account's own
    // profile), http_request gets the account's cookie jar, and the placeholders stand for its login.
    private async Task<string> SwitchUserAsync(JsonObject args, TestRun run, ThreadRecorder recorder)
    {
        if (run.Accounts is not { Accounts.Count: > 0 } set) return "Error: this project has no test accounts.";
        var name = PlannerToolRunner.Str(args, "account")?.Trim();
        if (set.Find(name) is not { } account)
            return $"Error: '{name}' is not a test account of this project. Accounts: {string.Join(", ", set.Accounts.Select(a => a.Username))}.";
        if (run.Account?.Id == account.Id) return $"You already act as {account.Username}.";

        if (run.Browser is { } browser)
        {
            run.BrowserProblems.AddRange(browser.Problems.Select(run.Mask));
            await browser.DisposeAsync();
            run.Browser = null;
        }
        run.Switch(account, CreateClient);
        if (!run.Report.Accounts.Contains(account.Username, StringComparer.OrdinalIgnoreCase)) run.Report.Accounts.Add(account.Username);
        var defaultPassword = string.IsNullOrEmpty(set.Settings.DevPassword);
        if (defaultPassword) run.Report.DefaultDevPassword = true;
        recorder.Log(ActivityLevel.Info, Lang.T($"Tester berganti ke akun uji {account.Username}", $"The Tester switched to test account {account.Username}") + (account.Role is null ? "." : $" ({account.Role})."));

        var answer = $"You now act as {account.Username}{(account.Role is null ? string.Empty : $" ({account.Role})")}. " +
                     $"{TestLogin.UsernamePlaceholder} is {account.Username} and {TestLogin.PasswordPlaceholder} is the project's development password" +
                     (defaultPassword ? " (a default one: the project has none set)" : string.Empty) + ". ";
        if (run.Test.Level == TestPlan.Ui)
            answer += "The next browser tool opens this account's own browser profile; a login made there in an earlier run may still be valid, so open the page first and log in only when the app asks. ";
        answer += "http_request now sends this account's cookies (log in through the app's login endpoint first when it needs one).";

        // "Login as": log in once as an account allowed to use it, in this account's profile, then enter as this account.
        if (set.Impersonation is { IsAvailable: true } info && !set.Impersonators().Any(a => a.Id == account.Id)
            && set.Impersonators().FirstOrDefault() is { } admin)
        {
            answer += $" Faster when the app does not ask for {account.Username}'s login by itself: log in here as {admin.Username} (username typed as it is, password {TestLogin.PasswordPlaceholder}), " +
                      $"then use login as: {info.How!.Replace("{{target}}", account.Username, StringComparison.OrdinalIgnoreCase)}" +
                      (string.IsNullOrWhiteSpace(info.Back) ? "." : $" (back to the admin: {info.Back}).") +
                      " When that does not work, log out and log in as this account normally.";
        }
        return answer;
    }
}
