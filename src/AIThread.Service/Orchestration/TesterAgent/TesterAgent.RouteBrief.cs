using System.Text;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The endpoints of the code the test is about: the routes of the changed files, the ones the scenarios target, and
    // for a planned target no route serves, the route it is clearly meant for (Corrections, by the target as written).
    private static (string? Section, Dictionary<string, (string Method, string From, string To)> Corrections) RouteBrief(
        TestingInput input, IReadOnlyList<CodeRoute> routes)
    {
        var corrections = new Dictionary<string, (string Method, string From, string To)>(StringComparer.Ordinal);
        if (routes.Count == 0) return (null, corrections);

        var changed = input.Execution.Results.Select(r => r.File)
            .Concat(input.Plan.Tasks.Select(t => t.File))
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f!.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = new List<CodeRoute>();
        void Show(IEnumerable<CodeRoute> more) => shown.AddRange(more.Where(r => !shown.Contains(r)));

        foreach (var scenario in input.Plan.Test!.Scenarios)
        {
            if (AspNetRoutes.SplitTarget(scenario.Target) is not { } target) continue;
            var matching = AspNetRoutes.Matching(target.Method, target.Path, routes);
            if (matching.Count > 0)
            {
                Show(matching);
                continue;
            }
            if (AspNetRoutes.Correction(target.Method, target.Path, routes) is { } correction)
            {
                corrections[scenario.Target!] = (target.Method, target.Path, correction.Path);
                Show([correction.Route]);
            }
            else Show(AspNetRoutes.Closest(target.Method, target.Path, routes));
        }
        Show(routes.Where(r => changed.Any(f => f.EndsWith(r.File, StringComparison.OrdinalIgnoreCase) || r.File.EndsWith(f, StringComparison.OrdinalIgnoreCase))));
        if (shown.Count == 0) return (null, corrections);

        var builder = new StringBuilder("\n# Endpoints in the code\n");
        builder.Append("Read from the route attributes after the change ([controller] = the class name without \"Controller\", [action] = the method name). Call these paths as written; {name} is a value you fill in. ANY: the action takes every method.\n");
        foreach (var route in shown.Take(MaxBriefRoutes))
        {
            builder.Append($"- {route.Method} {route.Path} — {route.Handler} ({route.File}:{route.Line})");
            if (route.Conventional) builder.Append(" — no route attribute: assumes the default {controller}/{action} route");
            builder.Append('\n');
        }
        if (shown.Count > MaxBriefRoutes) builder.Append($"... ({shown.Count - MaxBriefRoutes} more)\n");
        return (builder.ToString(), corrections);
    }
}
