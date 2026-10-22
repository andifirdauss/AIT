using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

// The content and file name of a scenario sheet to download.
public sealed record ScenarioFile(byte[] Content, string FileName);

// Multi-step test scenarios of a project (Project > Detail → Skenario test): made from a tester's suggestion, completed
// in an Excel sheet AIT generates, and run by the thread orchestrator. Shared by the team through the project key.
public interface ITestScenarioService
{
    Task<IReadOnlyList<TestingScenarioDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    // The scenario of a suggestion of a finished thread; the same one again when it was made before.
    Task<TestingScenarioDto> CreateFromFollowUpAsync(Guid threadId, int number, CancellationToken cancellationToken = default);

    Task<ScenarioFile> ExcelAsync(Guid projectId, Guid scenarioId, CancellationToken cancellationToken = default);

    // A filled sheet: its accounts that are new become test accounts of the project, its steps replace the scenario's.
    // Throws with the rows to fix when the sheet cannot be used.
    Task<ScenarioImportResultDto> ImportAsync(Guid projectId, Stream content, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid projectId, Guid scenarioId, CancellationToken cancellationToken = default);

    // By slug (or id text) within a project; null when there is none.
    Task<TestingScenario?> FindAsync(string projectKey, string slugOrId, CancellationToken cancellationToken = default);

    Task<TestingScenario?> GetAsync(Guid scenarioId, CancellationToken cancellationToken = default);

    // Writes the sheet to .aithread/testing/<slug>.xlsx in the project; returns that path relative to the root.
    Task<string> WriteToProjectAsync(ProjectContext project, Guid scenarioId, CancellationToken cancellationToken = default);

    // Imports .aithread/testing/<slug>.xlsx when the project has it; null when it does not.
    Task<ScenarioImportResultDto?> ImportFromProjectAsync(ProjectContext project, TestingScenario scenario, CancellationToken cancellationToken = default);

    Task SaveRunAsync(Guid scenarioId, TestingReport report, string by, CancellationToken cancellationToken = default);
}
