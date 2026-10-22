using System.Text;
using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Testing;

// The JSON parts of a TestingScenario, its DTO, a scenario made from a tester's suggestion, and its file name.
public static class ScenarioText
{
    public const int MaxSteps = 30;
    public const int MaxFields = 20;

    // Free-text values the tester types in a scenario run start with this, so test data is easy to find and clean up.
    public const string DataMarker = "[AIT TEST]";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<ScenarioStepDto> Steps(TestingScenario scenario) => Read<List<ScenarioStepDto>>(scenario.Steps) ?? [];
    public static IReadOnlyList<ScenarioRoleDto> Roles(TestingScenario scenario) => Read<List<ScenarioRoleDto>>(scenario.Roles) ?? [];
    public static IReadOnlyList<string> Fields(TestingScenario scenario) => Read<List<string>>(scenario.Fields) ?? [];

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static TestingReport? LastRun(TestingScenario scenario) => TestingReport.FromJson(scenario.LastRun);

    public static TestingScenarioDto ToDto(TestingScenario scenario) =>
        new(scenario.Id, scenario.Slug, scenario.Title, scenario.Description, scenario.Version, scenario.Level, scenario.BaseUrl, Steps(scenario), Roles(scenario),
            Fields(scenario), scenario.SourceThreadId, scenario.SourceFollowUp, Missing(scenario), LastRun(scenario)?.Outcome, scenario.LastRunAt, scenario.LastRunBy,
            scenario.CreatedBy, scenario.UpdatedBy, scenario.CreatedAt, scenario.UpdatedAt);

    // What still blocks a run, for the developer: a role without an account, or no step.
    public static IReadOnlyList<string> Missing(TestingScenario scenario)
    {
        var missing = Roles(scenario).Where(r => string.IsNullOrWhiteSpace(r.Username)).Select(r => $"Peran {r.Role} belum punya akun uji (sheet Akun).").ToList();
        if (Steps(scenario).Count == 0) missing.Add("Belum ada langkah (sheet Langkah).");
        return missing;
    }

    // A scenario from a suggestion: its scenarios become steps, its actors roles (an actor that is a username plays its
    // account's role; a role gets the first account that has it), its fields the form columns.
    public static TestingScenario FromFollowUp(TestFollowUp followUp, TestPlan test, TestAccountSet? accounts, string projectKey, string slug, Guid threadId, string by)
    {
        var roles = new List<ScenarioRoleDto>();
        var steps = new List<ScenarioStepDto>();
        foreach (var scenario in followUp.Scenarios.Take(MaxSteps))
        {
            var role = RoleOf(scenario.Actor, accounts, roles);
            steps.Add(new ScenarioStepDto(steps.Count + 1, role, scenario.Action ?? "lainnya", scenario.Target,
                scenario.Steps.Count == 0 ? scenario.Title : $"{scenario.Title}: {string.Join(" ", scenario.Steps)}", new Dictionary<string, string>(), scenario.Expect));
        }
        return new TestingScenario
        {
            ProjectKey = projectKey,
            Slug = slug,
            Title = Clip(followUp.Title, TestingScenario.MaxTitleLength),
            Description = followUp.Why is null ? followUp.Needs : followUp.Needs is null ? followUp.Why : $"{followUp.Why} Perlu: {followUp.Needs}",
            Level = test.Level,
            BaseUrl = test.BaseUrl,
            Notes = test.Notes,
            Steps = Write(steps),
            Roles = Write(roles),
            Fields = Write(followUp.Fields.Take(MaxFields).ToList()),
            SourceThreadId = threadId,
            SourceFollowUp = followUp.Number,
            CreatedBy = by,
            UpdatedBy = by
        };
    }

    // "Approve level 1 oleh atasan" → "approve-level-1-oleh-atasan"; "-2", "-3" ... when taken.
    public static string Slug(string title, IReadOnlyCollection<string> taken)
    {
        var builder = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormKD).ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-' && (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c))) builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        if (slug.Length == 0) slug = "skenario";
        var unique = slug;
        for (var n = 2; taken.Contains(unique, StringComparer.OrdinalIgnoreCase); n++) unique = $"{slug}-{n}";
        return unique;
    }

    // The account role an actor stands for, added to roles with its account when it is new.
    private static string? RoleOf(string? actor, TestAccountSet? accounts, List<ScenarioRoleDto> roles)
    {
        if (string.IsNullOrWhiteSpace(actor)) return null;
        var account = accounts?.Find(actor);
        var role = account is not null ? account.Role ?? account.Username : actor.Trim();
        if (roles.Any(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase))) return roles.First(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase)).Role;
        account ??= accounts?.Accounts.FirstOrDefault(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase));
        roles.Add(new ScenarioRoleDto(role, account?.Username));
        return role;
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max];
}
