using System.Text;
using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Service.Memory;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Skills;
using AIThread.Service.Verification;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestScanner
{
    // Title, purpose, scenario, module and targets for tests that have no purpose yet, a batch per model call. Stops
    // quietly when no node can answer; what was labeled stays.
    private async Task<int> LabelAsync(ProjectContext project, IReadOnlyList<TestSuite> suites, CancellationToken cancellationToken)
    {
        var tests = (await catalog.ListAsync(project.Key, cancellationToken))
            .Where(t => t.State != UnitTestState.Removed && string.IsNullOrWhiteSpace(t.Purpose))
            .Take(LabelBatchSize * MaxLabelBatches)
            .ToList();
        if (tests.Count == 0 || !await ai.IsAvailableAsync(cancellationToken)) return 0;

        var labeled = 0;
        foreach (var batch in tests.Chunk(LabelBatchSize))
        {
            var input = new StringBuilder($"# Project\n{project.Name}\n\n# Tests\n");
            for (var i = 0; i < batch.Length; i++)
            {
                var test = batch[i];
                input.Append($"\n## T{i + 1}\nFile: {test.TestFile}\nName: {test.MethodName}\n");
                if (test.ClassName is not null) input.Append($"Class: {test.ClassName}\n");
                if (test.Module is not null) input.Append($"Module (code map): {test.Module}\n");
                if (test.TargetFiles is not null) input.Append($"Likely targets: {string.Join(", ", UnitTestText.Lines(test.TargetFiles))}\n");
                if (Source(project.RootPath, test, suites) is { } source) input.Append("```\n").Append(source).Append("\n```\n");
            }

            JsonNode? json;
            try
            {
                (json, _) = await ai.AskJsonAsync(AgentPrompts.UnitTestLabeler, input.ToString(), 4_000, cancellationToken);
            }
            catch (Exception ex) when (ex is MemoryAiUnavailableException or NineRouterException)
            {
                logger.LogWarning(ex, "Unit tests of {Project} could not be labeled.", project.Name);
                break;
            }

            var changed = new List<UnitTestCase>();
            foreach (var item in json?["tests"] as JsonArray ?? [])
            {
                var id = SkillAi.Text(item?["id"]);
                if (id is null || !id.StartsWith('T') || !int.TryParse(id[1..], out var index) || index < 1 || index > batch.Length) continue;
                var test = batch[index - 1];
                if (SkillAi.Text(item?["title"]) is { Length: > 0 } title) test.Title = UnitTestText.Clip(title.Trim(), 300);
                if (SkillAi.Text(item?["purpose"]) is { Length: > 0 } purpose) test.Purpose = UnitTestText.Clip(purpose.Trim(), 2_000);
                if (SkillAi.Text(item?["scenario"]) is { Length: > 0 } scenario) test.Scenario = UnitTestText.Clip(scenario.Trim(), 4_000);
                if (MemoryLabels.Module(SkillAi.Text(item?["module"])) is { } module) test.Module = module;
                var targets = MemoryAi.Strings(item?["targets"]).Select(t => t.Trim().Trim('`').Replace('\\', '/'))
                    .Where(t => File.Exists(Path.Combine(project.RootPath, t))).ToList();
                if (targets.Count > 0) test.TargetFiles = UnitTestText.JoinLines(targets);
                changed.Add(test);
            }
            await catalog.UpdateAsync(changed, cancellationToken);
            labeled += changed.Count;
        }
        return labeled;
    }
}
