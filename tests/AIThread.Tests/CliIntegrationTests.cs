using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Implementations.Cli;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Tests;

public class ManagedTextBlockTests
{
    private static readonly string[] Block = ["KEY=\"value\""];

    [Theory]
    [InlineData("")]
    [InlineData("A=1\n")]
    [InlineData("A=1\r\nB=2\r\n")]
    public void Remove_restores_the_original_content(string original)
    {
        Assert.Equal(original, ManagedTextBlock.Remove(ManagedTextBlock.Append(original, Block)));
    }

    [Fact]
    public void Appending_twice_keeps_one_block_at_the_end()
    {
        var twice = ManagedTextBlock.Append(ManagedTextBlock.Append("A=1\n", Block), Block);
        var lines = ManagedTextBlock.Lines(twice);

        Assert.Single(lines, l => l == ManagedTextBlock.Begin);
        Assert.Equal(ManagedTextBlock.End, lines[^1]);
        Assert.Equal(Block, ManagedTextBlock.Managed(twice));
    }
}

public class CodexIntegrationTests : IDisposable
{
    private const string Proxy = "http://localhost:5225";
    private readonly TempWorkspace _workspace = new();
    private readonly string _config;
    private readonly CodexIntegration _integration;

    public CodexIntegrationTests()
    {
        _config = Path.Combine(_workspace.Root, "codex", "config.toml");
        _integration = new CodexIntegration(new CliTestSettings("ait-secret"), _config);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Apply_adds_provider_and_profile_and_revert_restores_the_file()
    {
        const string original = "model = \"o3\"\n\n[model_providers.ollama]\nname = \"Ollama\"\nbase_url = \"http://localhost:11434/v1\"\n";
        _workspace.Write("codex/config.toml", original);

        var status = await _integration.ApplyAsync(CliScope.Global, null, Proxy + "/");

        Assert.True(status.Enabled);
        var text = await File.ReadAllTextAsync(_config);
        Assert.StartsWith(original, text);
        Assert.Contains("[model_providers.aithread]\nname = \"AIT\"\nbase_url = \"http://localhost:5225/v1\"\nwire_api = \"responses\"", text);
        Assert.Contains("experimental_bearer_token = \"ait-secret\"", text);
        Assert.Contains("[profiles.aithread]\nmodel_provider = \"aithread\"", text);

        await _integration.RevertAsync(CliScope.Global, null, Proxy);
        Assert.Equal(original, await File.ReadAllTextAsync(_config));
    }

    [Fact]
    public async Task Revert_deletes_a_file_that_only_held_the_entry()
    {
        await _integration.ApplyAsync(CliScope.Global, null, Proxy);

        var status = await _integration.RevertAsync(CliScope.Global, null, Proxy);

        Assert.False(status.Enabled);
        Assert.False(File.Exists(_config));
    }

    [Fact]
    public async Task Other_port_is_reported_and_project_scope_is_refused()
    {
        await _integration.ApplyAsync(CliScope.Global, null, "http://localhost:5299");

        var status = await _integration.GetStatusAsync(CliScope.Global, null, Proxy);

        Assert.False(status.Enabled);
        Assert.Equal("http://localhost:5299/v1", status.CurrentBaseUrl);
        Assert.Null(_integration.Info.ProjectFile);
        await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Project, Guid.NewGuid(), Proxy));
    }

    [Theory]
    [InlineData("[model_providers.aithread]\nname = \"x\"\n")]
    [InlineData("[profiles.\"aithread\"]\nmodel = \"o3\"\n")]
    [InlineData("[model_providers]\naithread = { name = \"x\" }\n")]
    [InlineData("model_providers = { x = { name = \"x\" } }\n")]
    [InlineData("profiles.aithread.model = \"o3\"\n")]
    public void Unsafe_files_are_refused(string content)
    {
        Assert.NotNull(CodexIntegration.Conflict(content));
    }

    [Theory]
    [InlineData("[model_providers.aithreadx]\nname = \"x\"\n")]
    [InlineData("[profiles.fast]\naithread = true\n")]
    [InlineData("# [model_providers.aithread]\n")]
    public void Similar_names_are_not_a_conflict(string content)
    {
        Assert.Null(CodexIntegration.Conflict(content));
    }
}

public class GeminiCliIntegrationTests : IDisposable
{
    private const string Proxy = "http://localhost:5225";
    private readonly TempWorkspace _workspace = new();
    private readonly string _env;
    private readonly CliTestProjects _projects;
    private readonly GeminiCliIntegration _integration;

    public GeminiCliIntegrationTests()
    {
        _env = Path.Combine(_workspace.Root, "home", ".gemini", ".env");
        _projects = new CliTestProjects(Path.Combine(_workspace.Root, "repo"));
        _integration = new GeminiCliIntegration(new CliTestSettings(null), _projects, _env);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Global_apply_and_revert_keep_other_variables()
    {
        const string original = "GOOGLE_CLOUD_PROJECT=\"demo\"\n";
        _workspace.Write("home/.gemini/.env", original);

        var status = await _integration.ApplyAsync(CliScope.Global, null, Proxy);

        Assert.True(status.Enabled);
        var text = await File.ReadAllTextAsync(_env);
        Assert.Contains("GOOGLE_GEMINI_BASE_URL=\"http://localhost:5225\"", text);
        Assert.Contains("GEMINI_API_KEY=\"aithread-local\"", text);

        await _integration.RevertAsync(CliScope.Global, null, Proxy);
        Assert.Equal(original, await File.ReadAllTextAsync(_env));
    }

    [Fact]
    public async Task Own_api_key_is_refused()
    {
        _workspace.Write("home/.gemini/.env", "export GEMINI_API_KEY=abc\n");

        var error = await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Global, null, Proxy));

        Assert.Contains("GEMINI_API_KEY", error.Message);
    }

    [Fact]
    public async Task Project_scope_writes_inside_the_project()
    {
        await _integration.ApplyAsync(CliScope.Project, _projects.Project.Id, Proxy);

        var path = Path.Combine(_projects.Project.RootPath, ".gemini", ".env");
        Assert.Contains("GOOGLE_GEMINI_BASE_URL=\"http://localhost:5225\"", await File.ReadAllTextAsync(path));
        var statuses = await _integration.GetProjectStatusesAsync(Proxy);
        Assert.True(Assert.Single(statuses).Enabled);
        Assert.False(File.Exists(_env));
    }
}

public class OpenCodeIntegrationTests : IDisposable
{
    private const string Proxy = "http://localhost:5225";
    private readonly TempWorkspace _workspace = new();
    private readonly string _config;
    private readonly OpenCodeIntegration _integration;

    public OpenCodeIntegrationTests()
    {
        _config = Path.Combine(_workspace.Root, "opencode", "opencode.json");
        _integration = new OpenCodeIntegration(new CliTestSettings("ait-secret"), _config);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Apply_adds_the_provider_next_to_existing_ones_and_revert_removes_it()
    {
        _workspace.Write("opencode/opencode.json", "{ \"theme\": \"dark\", \"provider\": { \"ollama\": { \"npm\": \"@ai-sdk/openai-compatible\" } } }");

        var status = await _integration.ApplyAsync(CliScope.Global, null, Proxy);

        Assert.True(status.Enabled);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(_config))!;
        Assert.Equal("@ai-sdk/anthropic", (string?)root["provider"]!["aithread"]!["npm"]);
        Assert.Equal("http://localhost:5225/v1", (string?)root["provider"]!["aithread"]!["options"]!["baseURL"]);
        Assert.Equal("ait-secret", (string?)root["provider"]!["aithread"]!["options"]!["apiKey"]);
        Assert.NotNull(root["provider"]!["ollama"]);

        await _integration.RevertAsync(CliScope.Global, null, Proxy);
        root = JsonNode.Parse(await File.ReadAllTextAsync(_config))!;
        Assert.Null(root["provider"]!["aithread"]);
        Assert.Equal("dark", (string?)root["theme"]);
    }

    [Fact]
    public async Task Revert_deletes_a_file_that_only_held_the_provider()
    {
        await _integration.ApplyAsync(CliScope.Global, null, Proxy);
        await _integration.RevertAsync(CliScope.Global, null, Proxy);

        Assert.False(File.Exists(_config));
    }

    [Fact]
    public async Task Comments_and_a_user_provider_named_aithread_are_refused()
    {
        _workspace.Write("opencode/opencode.json", "{\n  // mine\n  \"theme\": \"dark\"\n}");
        var commented = await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Global, null, Proxy));
        Assert.Contains("komentar", commented.Message);
        // Nothing of ours in it: reverting leaves the commented file alone.
        await _integration.RevertAsync(CliScope.Global, null, Proxy);
        Assert.Contains("// mine", await File.ReadAllTextAsync(_config));

        _workspace.Write("opencode/opencode.json", "{ \"provider\": { \"aithread\": { \"name\": \"Mine\" } } }");
        var own = await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Global, null, Proxy));
        Assert.Contains("buatan Anda", own.Message);
    }
}

public class QwenCodeIntegrationTests : IDisposable
{
    private const string Proxy = "http://localhost:5225";
    private readonly TempWorkspace _workspace = new();
    private readonly string _settings;
    private readonly QwenCodeIntegration _integration;

    public QwenCodeIntegrationTests()
    {
        _settings = Path.Combine(_workspace.Root, "qwen", "settings.json");
        _integration = new QwenCodeIntegration(new CliTestSettings("ait-secret"), _settings);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Apply_adds_the_model_and_key_and_revert_removes_only_those()
    {
        _workspace.Write("qwen/settings.json",
            "{ \"modelProviders\": { \"openai\": [ { \"id\": \"gpt-4o\", \"envKey\": \"OPENAI_API_KEY\" } ] }, \"env\": { \"OPENAI_API_KEY\": \"sk\" } }");

        var status = await _integration.ApplyAsync(CliScope.Global, null, Proxy);
        await _integration.ApplyAsync(CliScope.Global, null, Proxy);

        Assert.True(status.Enabled);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(_settings))!;
        var models = root["modelProviders"]!["openai"]!.AsArray();
        Assert.Equal(2, models.Count);
        Assert.Equal("http://localhost:5225/v1", (string?)models[1]!["baseUrl"]);
        Assert.Equal("AITHREAD_API_KEY", (string?)models[1]!["envKey"]);
        Assert.Equal("ait-secret", (string?)root["env"]!["AITHREAD_API_KEY"]);

        await _integration.RevertAsync(CliScope.Global, null, Proxy);
        root = JsonNode.Parse(await File.ReadAllTextAsync(_settings))!;
        Assert.Equal("gpt-4o", (string?)Assert.Single(root["modelProviders"]!["openai"]!.AsArray())!["id"]);
        Assert.Null(root["env"]!["AITHREAD_API_KEY"]);
        Assert.Equal("sk", (string?)root["env"]!["OPENAI_API_KEY"]);
    }

    [Fact]
    public async Task Revert_deletes_a_file_that_only_held_the_model()
    {
        await _integration.ApplyAsync(CliScope.Global, null, Proxy);
        await _integration.RevertAsync(CliScope.Global, null, Proxy);

        Assert.False(File.Exists(_settings));
    }

    [Fact]
    public async Task A_user_model_with_the_same_id_is_refused()
    {
        _workspace.Write("qwen/settings.json", "{ \"modelProviders\": { \"openai\": [ { \"id\": \"aithread\", \"name\": \"Mine\" } ] } }");

        await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Global, null, Proxy));
    }
}

internal sealed class CliTestSettings(string? proxyKey) : ISettingService
{
    public Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new RuntimeSettings("http://router.test", null, proxyKey, null, true, true, 24, 1, 8, 60));
    public Task<SystemSettingDto> GetAsync(string proxyBaseUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SystemSettingDto> UpdateAsync(UpdateSystemSettingRequest request, string proxyBaseUrl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<RuntimeSettings> UpdateRouterAsync(string baseUrl, string? apiKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TelegramSettingsDto> GetTelegramAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<TelegramSettingsDto> UpdateTelegramAsync(UpdateTelegramSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<RuntimeSettings> UpdateSkillsAsync(UpdateSkillSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<RuntimeSettings> UpdateBusinessAsync(UpdateBusinessSettingsRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

// One registered project at rootPath; only what the CLI menu reads.
internal sealed class CliTestProjects(string rootPath) : IProjectService
{
    public ProjectDto Project { get; } = new(
        Guid.NewGuid(), "repo", rootPath, [], null, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0, true, "local:repo", default, null);

    public Task<ProjectContext?> GetContextAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(projectId == Project.Id ? new ProjectContext(Project.Id, Project.Name, Project.RootPath, [], []) : null);

    public Task<IReadOnlyList<ProjectDto>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectDto>>([Project]);

    public Task<ProjectContext?> ResolveSessionAsync(string? sessionId, string? workingDirectory, string? projectHint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProjectDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProjectDto> RegisterAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<RegisterProjectsResultDto> RegisterManyAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid id, bool includeSharedMemory = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProjectDto?> SetKeyAsync(Guid id, string? key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RefreshKeysAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<FolderBrowseDto> BrowseAsync(string? path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<ProjectScanDto> ScanAsync(string path, int depth, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
