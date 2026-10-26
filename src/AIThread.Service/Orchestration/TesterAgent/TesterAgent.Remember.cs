using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private const int MaxValues = 30;
    private const int MaxValueChars = 500;

    private string Remember(JsonObject args, TestRun run, ThreadRecorder recorder)
    {
        var name = PlannerToolRunner.Str(args, "name")?.Trim() ?? string.Empty;
        var value = PlannerToolRunner.Str(args, "value")?.Trim() ?? string.Empty;
        if (!ValueName().IsMatch(name)) return "Error: \"name\" must be letters, digits and _, starting with a letter (at most 40), e.g. \"requestNo\".";
        if (TesterTools.ReservedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return $"Error: \"{name}\" is filled by AIT itself; choose another name.";
        if (value.Length == 0) return "Error: \"value\" is empty.";
        if (value.Length > MaxValueChars) return $"Error: \"value\" is longer than {MaxValueChars} characters.";
        if (!run.Report.Values.ContainsKey(name) && run.Report.Values.Count >= MaxValues) return $"Error: at most {MaxValues} values are kept.";

        run.Report.Values[name] = value;
        recorder.Log(ActivityLevel.Info, Lang.T($"Tester mengingat {name} = {value}", $"Tester remembers {name} = {value}"));
        return $"Remembered: write {{{{{name}}}}} in a later tool call (also in a follow-up test) for \"{value}\".";
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,39}$")]
    private static partial Regex ValueName();
}
