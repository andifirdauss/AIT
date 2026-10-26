using System.Text;
using AIThread.Common.Entities;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    private static string BuildInput(string module, string language, LongMemory? current, IReadOnlyList<LongMemory> others,
        IReadOnlyList<LongMemory> changes, IReadOnlyList<LongMemory> facts)
    {
        var builder = new StringBuilder();
        builder.Append("# Module\n").Append(module).Append("\n\n");
        builder.Append("# Language\n").Append(language).Append("\n\n");
        builder.Append("# Current state\n").Append(current?.Description ?? "(none)").Append("\n\n");
        // A module merged into this one brought its own summary: it is part of the current state.
        foreach (var other in others) builder.Append(other.Description).Append("\n\n");

        builder.Append("# New change entries\n");
        if (changes.Count == 0) builder.Append("(none)\n");
        foreach (var change in changes) builder.Append($"## {change.CreatedAt:yyyy-MM-dd}\n").Append(change.Description).Append("\n\n");

        if (facts.Count > 0)
        {
            builder.Append("\n# Older facts\n");
            foreach (var fact in facts) builder.Append($"- [{fact.Kind}] ").Append(fact.Description.Replace("\n", " / ")).Append('\n');
        }
        return builder.ToString();
    }
}
