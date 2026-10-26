using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Implementations.Cli;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;

namespace AIThread.Tests;

public class YamlProviderBlockTests
{
    private static readonly string[] Provider = ["aithread:", "  baseUrl: \"http://localhost:5225\"", "  models:", "    - id: aithread"];

    [Fact]
    public void Empty_file_gets_providers_key_inside_the_block()
    {
        var text = YamlProviderBlock.Apply(null, "aithread", Provider);
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        Assert.Equal(YamlProviderBlock.Begin, lines[0]);
        Assert.Equal("providers:", lines[1]);
        Assert.Equal("  aithread:", lines[2]);
        Assert.Equal("      - id: aithread", lines[5]);
        Assert.Equal(YamlProviderBlock.End, lines[^1]);
        Assert.Equal(string.Empty, YamlProviderBlock.Remove(text));
    }

    [Fact]
    public void Existing_providers_get_the_entry_with_their_own_indentation()
    {
        const string original = "theme: dark\nproviders:\n    ollama:\n        baseUrl: http://127.0.0.1:11434\n";

        var text = YamlProviderBlock.Apply(original, "aithread", Provider);

        Assert.Contains("providers:\n" + YamlProviderBlock.Begin + "\n    aithread:\n      baseUrl:", text);
        Assert.Contains("    ollama:\n        baseUrl: http://127.0.0.1:11434", text);
        Assert.Equal(original, YamlProviderBlock.Remove(text));
    }

    [Theory]
    [InlineData("theme: dark\n")]
    [InlineData("theme: dark\r\nmodel: x\r\n")]
    [InlineData("providers:\n  ollama:\n    api: openai-completions\n")]
    public void Remove_restores_the_original_content(string original)
    {
        Assert.Equal(original, YamlProviderBlock.Remove(YamlProviderBlock.Apply(original, "aithread", Provider)));
    }

    [Fact]
    public void Applying_twice_keeps_one_block()
    {
        var twice = YamlProviderBlock.Apply(YamlProviderBlock.Apply("providers:\n  x:\n    api: y\n", "aithread", Provider), "aithread", Provider);

        Assert.Single(twice.Split('\n'), l => l.Trim() == YamlProviderBlock.Begin);
    }

    [Theory]
    [InlineData("providers: {}\n", "satu baris")]
    [InlineData("providers:\n  aithread:\n    baseUrl: http://elsewhere\n", "buatan Anda")]
    public void Unsafe_files_are_refused(string content, string expected)
    {
        Assert.Contains(expected, YamlProviderBlock.Conflict(content, "aithread"));
    }

    [Fact]
    public void Nested_keys_with_the_same_name_are_not_a_conflict()
    {
        Assert.Null(YamlProviderBlock.Conflict("providers:\n  other:\n    aithread: true\n", "aithread"));
    }

    [Fact]
    public void Quote_escapes_yaml_specials()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", YamlProviderBlock.Quote("a\"b\\c"));
    }
}

public class OhMyPiProviderIntegrationTests : IDisposable
{
    private const string Proxy = "http://localhost:5225";
    private readonly TempWorkspace _workspace = new();
    private readonly string _models;
    private readonly OhMyPiProviderIntegration _integration;

    public OhMyPiProviderIntegrationTests()
    {
        _models = Path.Combine(_workspace.Root, "agent", "models.yml");
        _integration = new OhMyPiProviderIntegration(new StubSettings("ait-secret"), _models);
    }

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Apply_writes_the_provider_and_status_reports_enabled()
    {
        var status = await _integration.ApplyAsync(CliScope.Global, null, Proxy + "/");

        Assert.True(status.Enabled);
        var text = await File.ReadAllTextAsync(_models);
        Assert.Contains("baseUrl: \"http://localhost:5225\"", text);
        Assert.Contains("api: anthropic-messages", text);
        Assert.Contains("apiKey: \"ait-secret\"", text);
        Assert.Contains("  AIT:\n", text.Replace("\r\n", "\n"));
        Assert.Contains("- id: AIThread", text);
        Assert.Contains("name: AIThread", text);
        Assert.Null(_integration.Info.ProjectFile);
    }

    [Fact]
    public async Task Block_with_the_old_provider_id_is_not_enabled_until_applied_again()
    {
        _workspace.Write("agent/models.yml", string.Join("\n", YamlProviderBlock.Begin, "providers:", "  aithread:",
            $"    baseUrl: \"{Proxy}\"", "    models:", "      - id: aithread", YamlProviderBlock.End) + "\n");

        Assert.False((await _integration.GetStatusAsync(CliScope.Global, null, Proxy)).Enabled);

        var status = await _integration.ApplyAsync(CliScope.Global, null, Proxy);

        Assert.True(status.Enabled);
        Assert.DoesNotContain("aithread:", await File.ReadAllTextAsync(_models));
    }

    [Fact]
    public async Task Revert_deletes_a_file_that_only_held_the_provider()
    {
        await _integration.ApplyAsync(CliScope.Global, null, Proxy);

        var status = await _integration.RevertAsync(CliScope.Global, null, Proxy);

        Assert.False(status.Enabled);
        Assert.False(File.Exists(_models));
    }

    [Fact]
    public async Task Existing_models_yml_is_backed_up_and_restored_on_revert()
    {
        const string original = "providers:\n  ollama:\n    baseUrl: http://127.0.0.1:11434\n    api: openai-completions\n";
        _workspace.Write("agent/models.yml", original);

        await _integration.ApplyAsync(CliScope.Global, null, Proxy);
        await _integration.RevertAsync(CliScope.Global, null, Proxy);

        Assert.Equal(original, await File.ReadAllTextAsync(_models));
        Assert.Equal(original, await File.ReadAllTextAsync(_models + ".aithread-backup"));
    }

    [Fact]
    public async Task Provider_for_another_port_is_not_enabled()
    {
        await _integration.ApplyAsync(CliScope.Global, null, "http://localhost:5299");

        var status = await _integration.GetStatusAsync(CliScope.Global, null, Proxy);

        Assert.False(status.Enabled);
        Assert.Equal("http://localhost:5299", status.CurrentBaseUrl);
    }

    [Fact]
    public async Task Project_scope_and_old_models_json_are_refused()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Project, Guid.NewGuid(), Proxy));

        _workspace.Write("agent/models.json", "{}");
        var error = await Assert.ThrowsAsync<AppValidationException>(() => _integration.ApplyAsync(CliScope.Global, null, Proxy));
        Assert.Contains("models.json", error.Message);
        Assert.Empty(await _integration.GetProjectStatusesAsync(Proxy));
    }

    private sealed class StubSettings(string? proxyKey) : ISettingService
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
        public Task<RuntimeSettings> UpdateLanguageAsync(string language, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
