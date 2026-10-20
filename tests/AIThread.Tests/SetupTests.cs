using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace AIThread.Tests;

// Menu Setup: the status computed from the live state, the one-click node preset, and the team profile that carries a
// setup from one machine to another without its secrets.
public class SetupTests
{
    [Fact]
    public async Task Preset_builds_a_ready_pipeline_with_the_heavy_tools_on_the_tools_node()
    {
        await using var harness = await CreateAsync();

        var result = await harness.Service<ISetupService>().ApplyPresetAsync(new SetupPresetRequest("strong-model", "cheap-model"));

        Assert.All(result.Nodes, n => Assert.True(n.Created));
        var nodes = await harness.Nodes.GetAllAsync();
        Assert.Equal(["AIT Context Keeper", "AIT Executor", "AIT Planner", "AIT Tools Node", "AIT Wrapper"], nodes.Select(n => n.Name).Order());
        Assert.Equal("strong-model", Single(nodes, NodeRole.Planner).Model);
        Assert.All(nodes.Where(n => n.Role != NodeRole.Planner), n => Assert.Equal("cheap-model", n.Model));
        Assert.All(nodes, n => Assert.Equal(10, n.Priority));

        var tools = Single(nodes, NodeRole.ToolsNode);
        var delegations = Single(nodes, NodeRole.Planner).ToolDelegations!;
        Assert.Equal([PlannerTools.FindReferences, PlannerTools.ReadFile, PlannerTools.SearchText], delegations.Select(d => d.Tool).Order());
        Assert.All(delegations, d => Assert.Equal(tools.Id, d.ToolsNodeId));

        var pipeline = await harness.Nodes.GetPipelineStatusAsync();
        Assert.True(pipeline.IsReady);
        Assert.Empty(pipeline.Issues);
    }

    [Fact]
    public async Task Running_the_preset_again_updates_its_own_nodes_and_leaves_the_others_alone()
    {
        await using var harness = await CreateAsync();
        var setup = harness.Service<ISetupService>();
        var manual = await harness.AddNodeAsync(NodeRole.Executor, "manual-model");
        await setup.ApplyPresetAsync(new SetupPresetRequest("strong-model", "cheap-model"));

        var again = await setup.ApplyPresetAsync(new SetupPresetRequest("strong-2", null, ToolsNode: false, Reviewer: true));

        Assert.Equal(["AIT Reviewer"], again.Nodes.Where(n => n.Created).Select(n => n.Node.Name));
        var nodes = await harness.Nodes.GetAllAsync();
        Assert.Equal(7, nodes.Count);
        Assert.Equal("manual-model", nodes.Single(n => n.Id == manual.Id).Model);
        Assert.Equal("strong-2", Single(nodes, NodeRole.Planner).Model);
        Assert.Equal("strong-2", nodes.Single(n => n.Name == "AIT Executor").Model);
        Assert.Equal("strong-2", Single(nodes, NodeRole.Reviewer).Model);
        Assert.False(Single(nodes, NodeRole.ToolsNode).IsActive);
        Assert.Empty(Single(nodes, NodeRole.Planner).ToolDelegations ?? []);
        Assert.DoesNotContain((await harness.Nodes.GetPipelineStatusAsync()).Issues, i => i.Contains("tools node"));
    }

    [Fact]
    public async Task Status_follows_the_live_state()
    {
        await using var harness = await CreateAsync();
        var setup = harness.Service<ISetupService>();

        var before = await setup.GetStatusAsync("http://localhost:5225");

        Assert.False(before.IsComplete);
        Assert.Equal(["system", "router", "nodes", "project", "cli", "memory"], before.Steps.Select(s => s.Id));
        Assert.True(Step(before, "system").Done);
        Assert.False(Step(before, "router").Done);
        Assert.False(Step(before, "nodes").Done);
        Assert.True(Step(before, "project").Done);
        Assert.False(Step(before, "project").Required);
        Assert.False(Step(before, "memory").Done);

        await setup.ApplyPresetAsync(new SetupPresetRequest("strong-model", "cheap-model"));
        var after = await setup.GetStatusAsync("http://localhost:5225");

        Assert.True(Step(after, "nodes").Done);
        Assert.Equal(before.RequiredDone + 1, after.RequiredDone);
    }

    [Fact]
    public async Task Exported_profile_recreates_the_nodes_on_another_machine_without_the_secrets()
    {
        await using var lead = await CreateAsync();
        await lead.Service<ISetupService>().ApplyPresetAsync(new SetupPresetRequest("strong-model", "cheap-model"));
        var backup = await lead.Nodes.CreateAsync(new SaveNodeRequest("Executor cadangan", NodeRole.Executor, "backup-model", null, 4096, 2, 100, null, false));
        var main = (await lead.Nodes.GetAllAsync()).Single(n => n.Name == "AIT Executor");
        await lead.Nodes.UpdateAsync(main.Id, new SaveNodeRequest(main.Name, main.Role, main.Model, main.Temperature, main.MaxTokens, main.MaxConcurrency,
            main.Priority, "Ikuti konvensi tim.", true, null, backup.Id));
        await lead.Service<ISettingService>().UpdateRouterAsync("http://router.kantor:20128", "sk-rahasia-123");

        var profile = await lead.Service<ISetupProfileService>().ExportAsync();
        var json = JsonSerializer.Serialize(profile);
        Assert.DoesNotContain("sk-rahasia-123", json);
        Assert.Equal("http://router.kantor:20128", profile.Router!.BaseUrl);

        await using var developer = await CreateAsync();
        var profiles = developer.Service<ISetupProfileService>();
        var preview = await profiles.ImportAsync(new SetupProfileImportRequest(profile, Sections: WithoutRouter));

        Assert.True(preview.DryRun);
        Assert.Contains(preview.Changes, c => c.Section == SetupProfileSection.Nodes && c.Text == "Ditambah: Planner 'AIT Planner' (strong-model).");
        Assert.Empty(await developer.Nodes.GetAllAsync());

        var imported = await profiles.ImportAsync(new SetupProfileImportRequest(profile, Sections: WithoutRouter, DryRun: false));

        Assert.Empty(imported.Errors);
        var nodes = await developer.Nodes.GetAllAsync();
        Assert.Equal((await lead.Nodes.GetAllAsync()).Select(n => (n.Name, n.Model, n.IsActive)).Order(), nodes.Select(n => (n.Name, n.Model, n.IsActive)).Order());
        var executor = nodes.Single(n => n.Name == "AIT Executor");
        Assert.Equal("Executor cadangan", executor.FallbackNodeName);
        Assert.Equal("Ikuti konvensi tim.", executor.SystemPrompt);
        var tools = Single(nodes, NodeRole.ToolsNode);
        Assert.All(Single(nodes, NodeRole.Planner).ToolDelegations!, d => Assert.Equal(tools.Id, d.ToolsNodeId));
        Assert.True((await developer.Nodes.GetPipelineStatusAsync()).IsReady);

        var again = await profiles.ImportAsync(new SetupProfileImportRequest(profile, Sections: WithoutRouter));
        Assert.DoesNotContain(again.Changes, c => c.Section == SetupProfileSection.Nodes);
    }

    [Fact]
    public async Task Replace_mode_deactivates_the_nodes_the_profile_does_not_have()
    {
        await using var lead = await CreateAsync();
        await lead.Service<ISetupService>().ApplyPresetAsync(new SetupPresetRequest("strong-model", "cheap-model"));
        var profile = await lead.Service<ISetupProfileService>().ExportAsync();

        await using var developer = await CreateAsync();
        var old = await developer.AddNodeAsync(NodeRole.Executor, "old-model");
        var request = new SetupProfileImportRequest(profile, [SetupProfileSection.Nodes], SetupProfileNodeMode.Replace);

        var preview = await developer.Service<ISetupProfileService>().ImportAsync(request);
        Assert.Contains(preview.Changes, c => c.Text == $"Dinonaktifkan: Executor '{old.Name}' (old-model).");

        await developer.Service<ISetupProfileService>().ImportAsync(request with { DryRun = false });
        var nodes = await developer.Nodes.GetAllAsync();
        Assert.False(nodes.Single(n => n.Id == old.Id).IsActive);
        Assert.Equal(6, nodes.Count);
    }

    [Fact]
    public async Task A_new_memory_server_gets_the_typed_password_and_never_the_old_one()
    {
        var storage = new FakeMemoryStorage();
        await using var developer = await CreateAsync(storage);
        var shared = new SetupProfileStore(MemoryStoreProvider.SqlServer, "db.kantor", 1433, "AIThreadMemory", "ait", null);
        var builtIn = new SetupProfileStore(MemoryStoreProvider.Default, null, null, null, null, null);
        var profile = new SetupProfileFile(SetupProfileFile.FormatName, SetupProfileFile.CurrentVersion, DateTime.UtcNow, null, null, null, null,
            new SetupProfileMemory(builtIn, shared, shared), null, null);

        var preview = await developer.Service<ISetupProfileService>().ImportAsync(new SetupProfileImportRequest(profile));

        Assert.Equal([SetupProfileSection.Memory], preview.Sections);
        Assert.Contains(preview.Changes, c => c.Text == "Long memory: bawaan → SQL Server ait@db.kantor:1433/AIThreadMemory");
        Assert.Equal(["businessPassword", "longPassword"], preview.Secrets.Select(s => s.Id).Order());
        Assert.All(preview.Secrets, s => Assert.False(s.Stored));
        Assert.Null(storage.LastUpdate);

        await developer.Service<ISetupProfileService>().ImportAsync(new SetupProfileImportRequest(profile,
            Secrets: new SetupProfileSecrets(null, null, "pw-long", null), DryRun: false));

        var update = storage.LastUpdate!;
        Assert.Equal(("pw-long", false), (update.LongMemory.Password, update.LongMemory.ClearPassword));
        Assert.Equal((null, true), (update.BusinessMemory!.Password, update.BusinessMemory.ClearPassword));
        Assert.Equal(MemoryStoreProvider.Default, update.ShortMemory.Provider);
    }

    [Fact]
    public async Task A_profile_of_another_format_or_a_newer_version_is_refused()
    {
        await using var harness = await CreateAsync();
        var profiles = harness.Service<ISetupProfileService>();
        var memoryFile = new SetupProfileFile("aithread-memory", 1, DateTime.UtcNow, null, null, null, null, null, null, null);

        await Assert.ThrowsAsync<AIThread.Service.Models.AppValidationException>(() => profiles.ImportAsync(new SetupProfileImportRequest(memoryFile)));
        await Assert.ThrowsAsync<AIThread.Service.Models.AppValidationException>(() =>
            profiles.ImportAsync(new SetupProfileImportRequest(memoryFile with { Format = SetupProfileFile.FormatName, Version = 99 })));
    }

    private static readonly SetupProfileSection[] WithoutRouter =
        [SetupProfileSection.Orchestration, SetupProfileSection.Nodes, SetupProfileSection.Memory, SetupProfileSection.Business, SetupProfileSection.Skills];

    // The harness has no switchable short memory store; the memory storage settings are faked.
    private static Task<OrchestrationHarness> CreateAsync(FakeMemoryStorage? storage = null) =>
        OrchestrationHarness.CreateAsync(configureServices: services => services.AddSingleton<IMemoryStorageService>(storage ?? new FakeMemoryStorage()));

    private static NodeDto Single(IReadOnlyList<NodeDto> nodes, NodeRole role) => nodes.Single(n => n.Role == role);

    private static SetupStepDto Step(SetupStatusDto status, string id) => status.Steps.Single(s => s.Id == id);

    private sealed class FakeMemoryStorage : IMemoryStorageService
    {
        private static readonly MemoryStoreDto BuiltIn = new(MemoryStoreProvider.Default, null, null, null, null, false, null);
        private static readonly MemoryStoreStatusDto Connected = new(true, "bawaan");

        public UpdateMemoryStorageRequest? LastUpdate { get; private set; }

        private MemoryStorageSettingsDto Settings { get; set; } = new(BuiltIn, BuiltIn, Connected, Connected, BuiltIn, Connected);

        public Task<MemoryStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);

        public Task<MemoryStorageUpdateResultDto> UpdateAsync(UpdateMemoryStorageRequest request, CancellationToken cancellationToken = default)
        {
            LastUpdate = request;
            static MemoryStoreDto Saved(SaveMemoryStoreRequest s) =>
                new(s.Provider, s.Host, s.Port, s.Database, s.Username, !string.IsNullOrEmpty(s.Password), s.Options);
            Settings = new MemoryStorageSettingsDto(Saved(request.ShortMemory), Saved(request.LongMemory), Connected, Connected,
                Saved(request.BusinessMemory!), Connected);
            return Task.FromResult(new MemoryStorageUpdateResultDto(Settings, 0, 0, []));
        }

        public Task<MemoryStoreTestResultDto> TestAsync(TestMemoryStoreRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ApplySavedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
