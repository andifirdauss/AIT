using System.Text;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private const int MaxRequestsForModels = 10;

    // The test as the wrapper reads it.
    public static string ForModels(TestingReport report)
    {
        var builder = new StringBuilder($"Outcome: {report.Outcome} — level {report.Level}");
        if (report.BaseUrl is not null) builder.Append($" at {report.BaseUrl}");
        if (report.Browser is not null) builder.Append($", browser {report.Browser}");
        builder.Append('\n');
        if (report.Error is not null) builder.Append($"Could not run: {report.Error}\n");
        if (!string.IsNullOrWhiteSpace(report.AppOutput)) builder.Append($"Last output of the app AIT tried to start (already shown to the developer):\n{report.AppOutput}\n");
        if (report.AppStarted is not null) builder.Append($"The app was not running; AIT started it for the test with `{report.AppStarted}` and stopped it afterwards.\n");
        if (report.LoginUser is not null) builder.Append($"Logged in as {report.LoginUser} (login given by the developer).\n");
        if (report.LoginNeeded is not null)
            builder.Append($"Stopped to ask for a login: {report.LoginNeeded}")
                .Append(report.LoginUrl is null ? string.Empty : $" ({report.LoginUrl})")
                .Append(report.AskedLogin ? ". AIT asks the developer to reply in the chat with \"login <username> <password>\"; the test then runs again.\n" : ".\n");
        if (report.Summary is not null) builder.Append($"Tester summary: {report.Summary}\n");
        foreach (var scenario in report.Scenarios)
            builder.Append($"- [{scenario.Status}] {scenario.Title}").Append(string.IsNullOrWhiteSpace(scenario.Details) ? string.Empty : $": {scenario.Details}").Append('\n');
        if (report.Requests.Count > 0)
        {
            builder.Append("Requests:\n");
            foreach (var request in report.Requests.Take(MaxRequestsForModels))
                builder.Append($"- {request.Method} {request.Url} → {(request.Error is null ? request.Status?.ToString() : $"no response ({request.Error})")}\n");
        }
        if (report.BrowserErrors.Count > 0)
            builder.Append("Browser errors:\n").Append(string.Concat(report.BrowserErrors.Take(MaxRequestsForModels).Select(e => $"- {e}\n")));
        return builder.ToString();
    }
}
