using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Implementations.Cli;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;

namespace AiThread.Tests;

public class EnvFileBlockTests
{
    private static readonly KeyValuePair<string, string>[] Entries = [new("A", "1"), new("B", "two words")];

    [Fact]
    public void Apply_to_an_empty_file_writes_only_the_block()
    {
        var text = EnvFileBlock.Apply(null, Entries);

        Assert.StartsWith(EnvFileBlock.Begin, text);
        Assert.Contains("A=1", text);
        Assert.Contains("B=two words", text);
        Assert.Equal("1", EnvFileBlock.GetManaged(text, "A"));
    }

    [Fact]
    public void Apply_keeps_the_users_lines_and_the_files_line_endings()
    {
        var original = "KEEP=1\r\n# my note\r\nOTHER=2\r\n";

        var text = EnvFileBlock.Apply(original, Entries);

        Assert.StartsWith(original, text);
        // Every line break in the file, including the block's, is CRLF.
        Assert.Equal(text.Count(c => c == '\n'), text.Split("\r\n").Length - 1);
    }

    [Fact]
    public void Apply_twice_replaces_the_block_instead_of_stacking_it()
    {
        var once = EnvFileBlock.Apply("KEEP=1\n", Entries);
        var twice = EnvFileBlock.Apply(once, [new("A", "changed")]);

        Assert.Single(twice.Split('\n'), l => l.Trim() == EnvFileBlock.Begin);
        Assert.Equal("changed", EnvFileBlock.GetManaged(twice, "A"));
        Assert.Null(EnvFileBlock.GetManaged(twice, "B"));
        Assert.Contains("KEEP=1", twice);
    }

    [Theory]
    [InlineData("KEEP=1\n")]
    [InlineData("KEEP=1\r\n\r\nOTHER=2\r\n")]
    [InlineData("")]
    public void Remove_restores_the_original_content(string original)
    {
        var applied = EnvFileBlock.Apply(original, Entries);

        Assert.Equal(original, EnvFileBlock.Remove(applied));
    }

    [Fact]
    public void Unmanaged_definitions_are_found_outside_the_block_only()
    {
        var text = EnvFileBlock.Apply("export A=mine\n# B=commented\n", Entries);

        Assert.Equal("mine", EnvFileBlock.GetUnmanaged(text, "A"));
        Assert.Null(EnvFileBlock.GetUnmanaged(text, "B")); // only inside our block, or commented out
    }
}

public class OhMyPiIntegrationTests : IDisposable
{
    private const string Proxy = "http://localhost:5225";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aithread-omp-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Guid _projectId = Guid.NewGuid();
    private string _solution;

    public OhMyPiIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        _solution = Path.Combine(_root, "MyApp.sln");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string EnvPath => Path.Combine(_root, ".env");

    private OhMyPiIntegration Create() => new(new StubSettings(), new StubProjects(_projectId, _root, _solution));

    [Fact]
    public async Task Apply_writes_the_block_and_status_reports_enabled()
    {
        var status = await Create().ApplyAsync(CliScope.Project, _projectId, Proxy);

        var text = await File.ReadAllTextAsync(EnvPath);
        Assert.True(status.Enabled);
        Assert.Equal(EnvPath, status.Path);
        Assert.Contains($"ANTHROPIC_BASE_URL={Proxy}", text);
        Assert.Contains("ANTHROPIC_API_KEY=aithread-local", text);
        Assert.Contains($"ANTHROPIC_CUSTOM_HEADERS=X-AiThread-Solution: {_solution}", text);
    }

    [Fact]
    public async Task Revert_removes_a_file_that_only_held_the_block()
    {
        var cli = Create();
        await cli.ApplyAsync(CliScope.Project, _projectId, Proxy);

        var status = await cli.RevertAsync(CliScope.Project, _projectId, Proxy);

        Assert.False(File.Exists(EnvPath));
        Assert.False(status.Enabled);
    }

    [Fact]
    public async Task Existing_env_is_backed_up_and_restored_exactly_on_revert()
    {
        const string original = "OPENAI_API_KEY=sk-mine\n# note\n";
        await File.WriteAllTextAsync(EnvPath, original);
        var cli = Create();

        await cli.ApplyAsync(CliScope.Project, _projectId, Proxy);
        Assert.Equal(original, await File.ReadAllTextAsync(EnvPath + ".aithread-backup"));

        await cli.RevertAsync(CliScope.Project, _projectId, Proxy);
        Assert.Equal(original, await File.ReadAllTextAsync(EnvPath));
    }

    [Fact]
    public async Task Own_definition_outside_the_block_is_refused_not_overwritten()
    {
        const string original = "ANTHROPIC_BASE_URL=https://gateway.example\n";
        await File.WriteAllTextAsync(EnvPath, original);

        var error = await Assert.ThrowsAsync<AppValidationException>(() => Create().ApplyAsync(CliScope.Project, _projectId, Proxy));

        Assert.Contains("ANTHROPIC_BASE_URL", error.Message);
        Assert.Equal(original, await File.ReadAllTextAsync(EnvPath));

        var status = await Create().GetStatusAsync(CliScope.Project, _projectId, Proxy);
        Assert.False(status.Enabled);
        Assert.Equal("https://gateway.example", status.CurrentBaseUrl);
    }

    [Fact]
    public async Task Block_for_another_port_is_reported_as_not_enabled()
    {
        await Create().ApplyAsync(CliScope.Project, _projectId, "http://localhost:5299");

        var status = await Create().GetStatusAsync(CliScope.Project, _projectId, Proxy);

        Assert.False(status.Enabled);
        Assert.Equal("http://localhost:5299", status.CurrentBaseUrl);
    }

    [Fact]
    public async Task Solution_path_that_would_break_the_header_list_is_left_out()
    {
        _solution = Path.Combine(_root, "My,App", "MyApp.sln");

        await Create().ApplyAsync(CliScope.Project, _projectId, Proxy);

        var text = await File.ReadAllTextAsync(EnvPath);
        Assert.Contains("ANTHROPIC_BASE_URL=", text);
        Assert.DoesNotContain("ANTHROPIC_CUSTOM_HEADERS", text);
    }

    [Fact]
    public async Task Project_statuses_list_every_registered_project()
    {
        var cli = Create();
        Assert.False((await cli.GetProjectStatusesAsync(Proxy)).Single().Enabled);

        await cli.ApplyAsync(CliScope.Project, _projectId, Proxy);

        var status = (await cli.GetProjectStatusesAsync(Proxy)).Single();
        Assert.Equal(_projectId, status.ProjectId);
        Assert.True(status.Enabled);
    }

    private sealed class StubSettings : ISettingService
    {
        public Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RuntimeSettings("http://router.test", null, null, null, true, true, 24, 1, 8, 60));

        public Task<SystemSettingDto> GetAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SystemSettingDto> UpdateAsync(UpdateSystemSettingRequest request, string proxyBaseUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TelegramSettingsDto> GetTelegramAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TelegramSettingsDto> UpdateTelegramAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubProjects(Guid id, string root, string solution) : IProjectService
    {
        public Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProjectContext?>(projectId == id ? new ProjectContext(id, "MyApp", root, solution, []) : null);

        public Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectDto>>([new ProjectDto(id, "MyApp", solution, root, null, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0, true)]);

        public Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? solutionHint, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> solutionPaths, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FolderBrowseDto> BrowseAsync(string? path, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<SolutionScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
