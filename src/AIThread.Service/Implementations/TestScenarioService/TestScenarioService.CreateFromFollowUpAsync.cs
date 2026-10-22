using AIThread.Common.DTOs;
using AIThread.Common.Enums;
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
        var thread = await main.Threads.FirstOrDefaultAsync(t => t.Id == threadId, cancellationToken) ?? throw new AppValidationException("Thread tidak ditemukan.");
        if (thread.Status != ThreadStatus.Completed) throw new AppValidationException("Skenario hanya bisa dibuat dari thread yang sudah selesai.");
        if (thread.ProjectId is not { } projectId) throw new AppValidationException("Project thread ini sudah dihapus.");
        var report = TestingReport.FromJson(thread.TestingJson) ?? throw new AppValidationException("Thread ini belum punya hasil testing.");
        var followUp = report.FollowUps.FirstOrDefault(f => f.Number == number) ?? throw new AppValidationException($"Test lanjutan {number} tidak ada di thread ini.");
        if (!ThreadPlan.TryParse(thread.PlanJson ?? string.Empty, out var plan, out _) || plan?.Test is not { IsRunnable: true } test)
            throw new AppValidationException("Thread ini tidak punya rencana testing UI/API.");
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
        activityLog.Info(LogCategory, $"Skenario test \"{scenario.Title}\" dibuat dari test lanjutan {number}.", threadId, projectId);
        return ScenarioText.ToDto(scenario);
    }
}
