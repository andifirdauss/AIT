using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    public async Task<UnitTestRestoreResult> RestoreAsync(ProjectContext project, IReadOnlyList<UnitTestCase> tests, IReadOnlyList<TestSuite> suites,
        CancellationToken cancellationToken)
    {
        var root = project.RootPath;
        var missingFiles = new Dictionary<string, List<UnitTestCase>>(StringComparer.OrdinalIgnoreCase);
        var missingMethods = new List<UnitTestCase>();
        foreach (var group in tests.Where(t => t.State is UnitTestState.Active or UnitTestState.Quarantined).GroupBy(t => t.TestFile, StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(Full(root, group.Key)))
            {
                missingFiles[group.Key] = group.ToList();
                continue;
            }
            // Without a suite AIT cannot tell which tests the file holds; it is left alone.
            if (TestCommandResolver.SuiteFor(suites, root, group.Key) is not { } suite) continue;
            var found = TestDiscovery.InFile(root, group.Key, suite).Select(d => d.Selector).ToHashSet(StringComparer.Ordinal);
            missingMethods.AddRange(group.Where(t => !found.Contains(t.Selector)));
        }
        if (missingFiles.Count == 0 && missingMethods.Count == 0) return UnitTestRestoreResult.None;

        var items = new List<UnitTestRestoreItem>();
        var changed = new List<UnitTestCase>();

        // A test found under the same name in another file was moved, not lost: it follows its file.
        var discovered = await scanner.DiscoverAsync(project, suites, cancellationToken);
        var bySelector = discovered.GroupBy(d => d.Selector, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        bool Moved(UnitTestCase test)
        {
            if (!bySelector.TryGetValue(test.Selector, out var found) || string.Equals(found.File, test.TestFile, StringComparison.OrdinalIgnoreCase)) return false;
            items.Add(new UnitTestRestoreItem(test.DisplayNumber, found.File, UnitTestRestoreItem.Moved, Lang.T($"pindah dari {test.TestFile}", $"moved from {test.TestFile}")));
            test.TestFile = found.File;
            test.TestProject = found.TestProject;
            changed.Add(test);
            return true;
        }
        foreach (var file in missingFiles.Keys.ToList())
        {
            missingFiles[file].RemoveAll(Moved);
            if (missingFiles[file].Count == 0) missingFiles.Remove(file);
        }
        missingMethods.RemoveAll(Moved);

        var notes = await GitNotesAsync(project, missingFiles.Values.SelectMany(t => t).Concat(missingMethods).ToList(), cancellationToken);
        var inserts = new List<UnitTestCase>(missingMethods);
        var regenerate = new List<UnitTestCase>();

        // Layer 2: the whole file from the newest snapshot of its tests, plus the support files that are missing too.
        foreach (var (file, group) in missingFiles)
        {
            var source = group.Where(t => t.SourceSnapshot is not null).OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt).FirstOrDefault();
            if (source is null)
            {
                regenerate.AddRange(group);
                continue;
            }
            await WriteAsync(root, file, source.SourceSnapshot!, cancellationToken);
            foreach (var (path, content) in UnitTestSnapshot.SupportFiles(source.SupportFiles))
            {
                if (File.Exists(Full(root, path))) continue;
                await WriteAsync(root, path, content, cancellationToken);
                items.Add(new UnitTestRestoreItem(source.DisplayNumber, path, UnitTestRestoreItem.Snapshot, Lang.T("file pendukung", "supporting file")));
                if (IsProjectFile(path)) await AddToSolutionAsync(project, path, notes, cancellationToken);
            }

            var suite = TestCommandResolver.SuiteFor(suites, root, file);
            var restored = suite is null ? null : TestDiscovery.InContent(file, source.SourceSnapshot!, suite).Select(d => d.Selector).ToHashSet(StringComparer.Ordinal);
            foreach (var test in group)
            {
                if (restored is null || restored.Contains(test.Selector)) items.Add(new UnitTestRestoreItem(test.DisplayNumber, file, UnitTestRestoreItem.Snapshot));
                else inserts.Add(test);
            }
        }

        // A method missing from a file that exists: C# goes back from the snapshot without a model.
        var methodTasks = new List<UnitTestCase>();
        foreach (var test in inserts)
        {
            if (await InsertAsync(root, test, cancellationToken)) items.Add(new UnitTestRestoreItem(test.DisplayNumber, test.TestFile, UnitTestRestoreItem.Method));
            else if (test.MethodSnapshot is not null) methodTasks.Add(test);
            else regenerate.Add(test);
        }

        // Layer 3: an executor, from the method snapshot or, without any snapshot, from the scenario.
        var tasks = new List<PlanTask>();
        foreach (var group in methodTasks.GroupBy(t => t.TestFile, StringComparer.OrdinalIgnoreCase))
        {
            tasks.Add(InsertTask($"R{tasks.Count + 1}", group.Key, group.ToList()));
            items.AddRange(group.Select(t => new UnitTestRestoreItem(t.DisplayNumber, t.TestFile, UnitTestRestoreItem.Task)));
        }
        foreach (var group in regenerate.GroupBy(t => t.TestFile, StringComparer.OrdinalIgnoreCase))
        {
            var writable = group.Where(t => !string.IsNullOrWhiteSpace(t.Scenario)).ToList();
            items.AddRange(group.Where(t => string.IsNullOrWhiteSpace(t.Scenario))
                .Select(t => new UnitTestRestoreItem(t.DisplayNumber, t.TestFile, UnitTestRestoreItem.Missing, Lang.T("tidak ada snapshot maupun skenario untuk menulisnya lagi", "no snapshot or scenario to write it again"))));
            if (writable.Count == 0) continue;
            var suite = TestCommandResolver.SuiteFor(suites, root, group.Key);
            tasks.Add(RegenerateTask($"R{tasks.Count + 1}", group.Key, File.Exists(Full(root, group.Key)), writable, suite, Example(discovered, suite, group.Key)));
            foreach (var test in writable)
            {
                test.Origin = UnitTestOrigin.Regenerated;
                changed.Add(test);
                items.Add(new UnitTestRestoreItem(test.DisplayNumber, test.TestFile, UnitTestRestoreItem.Regenerate));
            }
        }

        await catalog.UpdateAsync(changed.Distinct().ToList(), cancellationToken);
        return new UnitTestRestoreResult(items, tasks, notes);
    }
}
