using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestScanner
{
    public async Task<UnitTestScanResultDto> ScanAsync(ProjectContext project, bool label, string by, CancellationToken cancellationToken)
    {
        var key = project.Key;
        if (string.IsNullOrEmpty(key)) throw new AppValidationException(Lang.T("Project ini belum punya project key.", "This project has no project key yet."));

        var settings = await catalog.GetSettingsAsync(key, cancellationToken);
        var suites = TestCommandResolver.Suites(project.RootPath, project.Modules);
        var warnings = suites.Where(s => !s.CanRun).Select(s => $"{s.Manifest}: {s.Problem}").ToList();
        if (suites.Count == 0) warnings.Add(Lang.T("Tidak ada test project atau runner test yang dikenali di project ini.", "No recognized test project or test runner in this project."));

        var discovered = await DiscoverAsync(project, suites, cancellationToken);
        var existing = await catalog.ListAsync(key, cancellationToken);
        var bySelector = existing.GroupBy(t => t.Selector, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Number).First(), StringComparer.Ordinal);
        var moved = new List<UnitTestCase>();
        var fresh = new List<UnitTestCase>();
        foreach (var test in discovered)
        {
            if (bySelector.TryGetValue(test.Selector, out var known))
            {
                if (!string.Equals(known.TestFile, test.File, StringComparison.OrdinalIgnoreCase))
                {
                    known.TestFile = test.File;
                    known.TestProject = test.TestProject;
                    moved.Add(known);
                }
                continue;
            }
            var entry = UnitTestEntries.FromDiscovered(test, key, project.Name, UnitTestOrigin.Scanned, by, null);
            fresh.Add(entry);
            bySelector[test.Selector] = entry;
        }
        var found = discovered.Select(d => d.Selector).ToHashSet(StringComparer.Ordinal);
        var missing = existing.Count(t => t.State == UnitTestState.Active && !found.Contains(t.Selector));

        if (fresh.Count > 0)
        {
            var files = Files(project, cancellationToken);
            var modules = (await codeMapper.MapQuicklyAsync(project, fresh.Select(t => t.TestFile).Distinct(StringComparer.OrdinalIgnoreCase), cancellationToken))
                .GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Module, StringComparer.OrdinalIgnoreCase);
            var (commit, branch) = settings.KeepSnapshots && GitTools.IsRepository(project.RootPath)
                ? await new GitTools(new WorkspaceScope(project.RootPath)).HeadAsync(cancellationToken)
                : (null, null);
            foreach (var test in fresh)
            {
                test.Module = modules.GetValueOrDefault(test.TestFile);
                test.TargetFiles = UnitTestText.JoinLines(UnitTestEntries.GuessTargets(test.TestFile, test.ClassName, files));
                if (settings.KeepSnapshots) UnitTestSnapshot.Capture(project.RootPath, test, TestCommandResolver.SuiteFor(suites, project.RootPath, test.TestFile), commit, branch);
            }
            await catalog.AddAsync(key, fresh, cancellationToken);
        }
        await catalog.UpdateAsync(moved, cancellationToken);

        var labeled = label ? await LabelAsync(project, suites, cancellationToken) : 0;
        return new UnitTestScanResultDto(discovered.Count, fresh.Count, moved.Count, moved.Count, missing, labeled, warnings);
    }
}
