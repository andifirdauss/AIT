using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.Testing;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class TestScenarioService
{
    public async Task<TestingScenarioDto> CreateFromFollowUpAsync(Guid threadId, int number, CancellationToken cancellationToken = default)
    {
        await using var main = await dbFactory.CreateDbContextAsync(cancellationToken);
        var thread = await main.Threads.FirstOrDefaultAsync(t => t.Id == threadId, cancellationToken) ?? throw new AppValidationException(Lang.T("Thread tidak ditemukan.", "Thread not found."));
        if (thread.Status != ThreadStatus.Completed) throw new AppValidationException(Lang.T("Skenario hanya bisa dibuat dari thread yang sudah selesai.", "A scenario can only be made from a finished thread."));
        if (thread.ProjectId is not { } projectId) throw new AppValidationException(Lang.T("Project thread ini sudah dihapus.", "The project of this thread has been deleted."));
        var report = TestingReport.FromJson(thread.TestingJson) ?? throw new AppValidationException(Lang.T("Thread ini belum punya hasil testing.", "This thread has no testing result yet."));
        var followUp = report.FollowUps.FirstOrDefault(f => f.Number == number) ?? throw new AppValidationException(Lang.T($"Test lanjutan {number} tidak ada di thread ini.", $"Follow-up test {number} is not in this thread."));
        if (!ThreadPlan.TryParse(thread.PlanJson ?? string.Empty, out var plan, out _) || plan?.Test is not { IsRunnable: true } test)
            throw new AppValidationException(Lang.T("Thread ini tidak punya rencana testing UI/API.", "This thread has no UI/API testing plan."));
        var (project, by) = await ProjectAsync(projectId, cancellationToken);

        await using var db = await longStore.CreateTestingAsync(cancellationToken);
        if (followUp.ScenarioId is { } existingId && await db.TestingScenarios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == existingId, cancellationToken) is { } existing)
            return ScenarioText.ToDto(existing);

        var key = project.Key;
        var taken = await db.TestingScenarios.AsNoTracking().Where(s => s.ProjectKey == key).Select(s => s.Slug).ToListAsync(cancellationToken);
        var scenario = ScenarioText.FromFollowUp(followUp, test, await accounts.GetAsync(key, cancellationToken), key, ScenarioText.Slug(followUp.Title, taken), threadId, by);
        db.TestingScenarios.Add(scenario);
        await db.SaveChangesAsync(cancellationToken);

        followUp.ScenarioId = scenario.Id;
        thread.TestingJson = report.ToJson();
        await main.SaveChangesAsync(cancellationToken);
        activityLog.Info(LogCategory, Lang.T($"Skenario test \"{scenario.Title}\" dibuat dari test lanjutan {number}.", $"Test scenario \"{scenario.Title}\" created from follow-up test {number}."), threadId, projectId);
        return ScenarioText.ToDto(scenario);
    }
}
