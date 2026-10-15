using System.Diagnostics;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed class NodeService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog) : INodeService
{
    public const int MaxSkillsPerNode = 10;

    public async Task<IReadOnlyList<NodeDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nodes = await WithDetails(db.Nodes.AsNoTracking()).ToListAsync(cancellationToken);
        var names = nodes.ToDictionary(n => n.Id, n => n.Name);
        return nodes.OrderBy(n => n.Role).ThenBy(n => n.Priority).ThenBy(n => n.Name).Select(n => ToDto(n, names)).ToList();
    }

    public async Task<NodeDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await WithDetails(db.Nodes.AsNoTracking()).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        return node is null ? null : ToDto(node, await FallbackNamesAsync(db, node, cancellationToken));
    }

    public async Task<NodeDto> CreateAsync(SaveNodeRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = new AiNode();
        Apply(node, request);
        await ValidateFallbackAsync(db, node, cancellationToken);
        await ApplySkillsAsync(db, node, request.SkillIds, cancellationToken);
        await ApplyDelegationsAsync(db, node, request.ToolDelegations, cancellationToken);
        db.Nodes.Add(node);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "created" }));
        var names = await FallbackNamesAsync(db, node, cancellationToken);
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' ditambahkan ({node.Model}){SkillNote(node)}{DelegationNote(node)}{FallbackNote(node, names)}.");
        return ToDto(node, names);
    }

    public async Task<NodeDto?> UpdateAsync(Guid id, SaveNodeRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await WithDetails(db.Nodes).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null) return null;

        var previousRole = node.Role;
        Apply(node, request);
        await ValidateFallbackAsync(db, node, cancellationToken);
        await ApplySkillsAsync(db, node, request.SkillIds, cancellationToken);
        await ApplyDelegationsAsync(db, node, request.ToolDelegations, cancellationToken);
        node.UpdatedAt = DateTime.UtcNow;

        // A node that changes role can no longer back up nodes of its old role, nor run tools for planners.
        var released = 0;
        var toolsReleased = 0;
        if (previousRole != node.Role)
        {
            var dependents = await db.Nodes.Where(n => n.FallbackNodeId == node.Id).ToListAsync(cancellationToken);
            foreach (var dependent in dependents) dependent.FallbackNodeId = null;
            released = dependents.Count;
            if (previousRole == NodeRole.ToolsNode) toolsReleased = await ReleaseDelegationsAsync(db, node.Id, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "updated" }));
        var names = await FallbackNamesAsync(db, node, cancellationToken);
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' diperbarui ({node.Model}){SkillNote(node)}{DelegationNote(node)}{FallbackNote(node, names)}." +
                                 (released > 0 ? $" {released} node lain tidak lagi memakainya sebagai cadangan karena role berubah." : string.Empty) +
                                 (toolsReleased > 0 ? $" {toolsReleased} delegasi tool planner dilepas dan kembali dijalankan planner." : string.Empty));
        return ToDto(node, names);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null) return false;

        // The foreign key does not null references itself (SQL Server forbids it on a self reference).
        var dependents = await db.Nodes.Where(n => n.FallbackNodeId == id).ToListAsync(cancellationToken);
        foreach (var dependent in dependents) dependent.FallbackNodeId = null;
        var toolsReleased = await ReleaseDelegationsAsync(db, id, cancellationToken);
        db.Nodes.Remove(node);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "deleted" }));
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' dihapus." +
                                 (toolsReleased > 0 ? $" {toolsReleased} delegasi tool planner dilepas dan kembali dijalankan planner." : string.Empty));
        return true;
    }

    public async Task<IReadOnlyList<AiNode>> GetActiveAsync(NodeRole role, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await WithDetails(db.Nodes.AsNoTracking())
            .Where(n => n.Role == role && n.IsActive)
            .OrderBy(n => n.Priority).ThenBy(n => n.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiNode?> GetNodeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await WithDetails(db.Nodes.AsNoTracking()).FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<PipelineStatusDto> GetPipelineStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var counts = await db.Nodes.AsNoTracking()
            .Where(n => n.IsActive)
            .GroupBy(n => n.Role)
            .Select(g => new { Role = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(NodeRole role) => counts.FirstOrDefault(c => c.Role == role)?.Count ?? 0;
        var planners = Count(NodeRole.Planner);
        var executors = Count(NodeRole.Executor);
        var wrappers = Count(NodeRole.Wrapper);

        var issues = new List<string>();
        if (planners == 0) issues.Add("Belum ada node Planner aktif.");
        if (executors == 0) issues.Add("Belum ada node Executor aktif.");
        if (wrappers == 0) issues.Add("Belum ada node Wrapper aktif.");
        if (wrappers > 1) issues.Add("Lebih dari satu Wrapper aktif — yang dipakai adalah prioritas terkecil.");
        if (Count(NodeRole.Reviewer) > 1) issues.Add("Lebih dari satu Reviewer aktif — yang dipakai adalah prioritas terkecil.");
        if (await db.PlannerToolDelegations.AnyAsync(d => d.PlannerNode!.IsActive && !d.ToolsNode!.IsActive, cancellationToken))
            issues.Add("Ada tool planner yang didelegasikan ke tools node nonaktif — tool itu dijalankan planner sendiri.");

        return new PipelineStatusDto(planners > 0 && executors > 0 && wrappers > 0, planners, executors, wrappers, issues);
    }

    public async Task<NodeTestResultDto> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var node = await GetAsync(id, cancellationToken);
        if (node is null) return new NodeTestResultDto(false, null, 0, 0, "Node tidak ditemukan.");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await router.ChatAsync(new ChatCompletionRequest
            {
                Model = node.Model,
                Messages = [ChatMessages.User("Reply with exactly one word: pong")],
                MaxTokens = 256,
                Temperature = node.Temperature
            }, cancellationToken: cancellationToken);

            stopwatch.Stop();
            return new NodeTestResultDto(true, result.Content.Trim(), stopwatch.ElapsedMilliseconds, result.TotalTokens, null);
        }
        catch (NineRouterException ex)
        {
            return new NodeTestResultDto(false, null, stopwatch.ElapsedMilliseconds, 0, ex.Message);
        }
    }

    private static void Validate(SaveNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new AppValidationException("Nama node wajib diisi.");
        if (request.Name.Length > 100) throw new AppValidationException("Nama node maksimal 100 karakter.");
        if (!Enum.IsDefined(request.Role)) throw new AppValidationException("Role node tidak valid.");
        if (string.IsNullOrWhiteSpace(request.Model)) throw new AppValidationException("Model wajib dipilih.");
        if (request.MaxTokens is < 256 or > 200_000) throw new AppValidationException("Max tokens harus di antara 256 dan 200000.");
        if (request.MaxConcurrency is < 1 or > 64) throw new AppValidationException("Max concurrency harus di antara 1 dan 64.");
        if (request.Temperature is < 0 or > 2) throw new AppValidationException("Temperature harus di antara 0 dan 2.");
    }

    private static void Apply(AiNode node, SaveNodeRequest request)
    {
        node.Name = request.Name.Trim();
        node.Role = request.Role;
        node.Model = request.Model.Trim();
        node.Temperature = request.Temperature;
        node.MaxTokens = request.MaxTokens;
        node.MaxConcurrency = request.MaxConcurrency;
        node.Priority = request.Priority;
        node.SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim();
        node.IsActive = request.IsActive;
        node.FallbackNodeId = request.FallbackNodeId;
    }

    // Same role, not itself, and following the chain never comes back to this node.
    private static async Task ValidateFallbackAsync(AiThreadDbContext db, AiNode node, CancellationToken cancellationToken)
    {
        if (node.FallbackNodeId is not { } fallbackId) return;
        if (fallbackId == node.Id) throw new AppValidationException("Node tidak bisa menjadi cadangan untuk dirinya sendiri.");

        var links = await db.Nodes.AsNoTracking()
            .Select(n => new { n.Id, n.Name, n.Role, n.FallbackNodeId })
            .ToDictionaryAsync(n => n.Id, cancellationToken);
        if (!links.TryGetValue(fallbackId, out var fallback)) throw new AppValidationException("Node cadangan tidak ditemukan.");
        if (fallback.Role != node.Role)
            throw new AppValidationException($"Node cadangan harus ber-role {node.Role}, sedangkan '{fallback.Name}' ber-role {fallback.Role}.");

        var path = new List<string> { node.Name };
        var visited = new HashSet<Guid> { node.Id };
        for (Guid? current = fallbackId; current is { } id && links.TryGetValue(id, out var link); current = link.FallbackNodeId)
        {
            path.Add(link.Name);
            if (link.FallbackNodeId == node.Id || !visited.Add(id))
                throw new AppValidationException($"Rantai cadangan berputar: {string.Join(" → ", path)} → {node.Name}. Pilih node cadangan lain.");
        }
    }

    private static async Task<Dictionary<Guid, string>> FallbackNamesAsync(AiThreadDbContext db, AiNode node, CancellationToken cancellationToken)
    {
        if (node.FallbackNodeId is not { } id) return [];
        var name = await db.Nodes.AsNoTracking().Where(n => n.Id == id).Select(n => n.Name).FirstOrDefaultAsync(cancellationToken);
        return name is null ? [] : new Dictionary<Guid, string> { [id] = name };
    }

    private static string FallbackNote(AiNode node, IReadOnlyDictionary<Guid, string> names) =>
        node.FallbackNodeId is { } id && names.TryGetValue(id, out var name) ? $", cadangan '{name}'" : string.Empty;

    // Skills with their source, in prompt order: the agents read their content, the UI their names. Tool delegations
    // with their tools node (without its skills: the planner loads a tools node through GetNodeAsync before calling it).
    private static IQueryable<AiNode> WithDetails(IQueryable<AiNode> nodes) =>
        nodes.Include(n => n.Skills.OrderBy(s => s.Order)).ThenInclude(s => s.Skill!).ThenInclude(s => s.SkillSource)
            .Include(n => n.ToolDelegations).ThenInclude(d => d.ToolsNode)
            .AsSplitQuery();

    // Only planners delegate; another role drops its delegations. null = leave them as they are.
    private static async Task ApplyDelegationsAsync(AiThreadDbContext db, AiNode node, IReadOnlyList<ToolDelegationRequest>? delegations, CancellationToken cancellationToken)
    {
        if (node.Role != NodeRole.Planner)
        {
            node.ToolDelegations.Clear();
            return;
        }
        if (delegations is null) return;

        var wanted = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var delegation in delegations)
        {
            var tool = delegation.Tool?.Trim() ?? string.Empty;
            if (!PlannerTools.Delegable.Contains(tool))
                throw new AppValidationException($"Tool '{tool}' tidak bisa didelegasikan. Pilihan: {string.Join(", ", PlannerTools.Delegable)}.");
            if (!wanted.TryAdd(tool, delegation.ToolsNodeId))
                throw new AppValidationException($"Tool '{tool}' didelegasikan lebih dari sekali; satu tool hanya untuk satu tools node.");
        }

        var toolsNodeIds = wanted.Values.Distinct().ToList();
        var toolsNodes = await db.Nodes.AsNoTracking().Where(n => toolsNodeIds.Contains(n.Id))
            .Select(n => new { n.Id, n.Name, n.Role }).ToDictionaryAsync(n => n.Id, cancellationToken);
        foreach (var toolsNodeId in toolsNodeIds)
        {
            if (!toolsNodes.TryGetValue(toolsNodeId, out var toolsNode)) throw new AppValidationException("Tools node untuk delegasi tool tidak ditemukan.");
            if (toolsNode.Role != NodeRole.ToolsNode)
                throw new AppValidationException($"Tool hanya bisa didelegasikan ke node ber-role ToolsNode, sedangkan '{toolsNode.Name}' ber-role {toolsNode.Role}.");
        }

        node.ToolDelegations.RemoveAll(d => !wanted.TryGetValue(d.Tool, out var toolsNodeId) || toolsNodeId != d.ToolsNodeId);
        foreach (var (tool, toolsNodeId) in wanted)
        {
            if (node.ToolDelegations.Any(d => d.Tool == tool)) continue;
            node.ToolDelegations.Add(new PlannerToolDelegation { PlannerNodeId = node.Id, Tool = tool, ToolsNodeId = toolsNodeId });
        }
    }

    // The foreign key to the tools node does not cascade (SQL Server allows one cascade path to Nodes).
    private static async Task<int> ReleaseDelegationsAsync(AiThreadDbContext db, Guid toolsNodeId, CancellationToken cancellationToken)
    {
        var delegations = await db.PlannerToolDelegations.Where(d => d.ToolsNodeId == toolsNodeId).ToListAsync(cancellationToken);
        db.PlannerToolDelegations.RemoveRange(delegations);
        return delegations.Count;
    }

    private static string DelegationNote(AiNode node) =>
        node.ToolDelegations.Count == 0 ? string.Empty : $", {node.ToolDelegations.Count} tool ke tools node";

    // null = leave the node's skills as they are (clients that do not know about skills).
    private static async Task ApplySkillsAsync(AiThreadDbContext db, AiNode node, IReadOnlyList<Guid>? skillIds, CancellationToken cancellationToken)
    {
        if (skillIds is null) return;
        var wanted = skillIds.Distinct().ToList();
        if (wanted.Count > MaxSkillsPerNode)
            throw new AppValidationException($"Satu node maksimal {MaxSkillsPerNode} skill — skill yang terlalu banyak membuat prompt boros dan tidak fokus.");

        var skills = await db.Skills.Include(s => s.SkillSource).Where(s => wanted.Contains(s.Id)).ToListAsync(cancellationToken);
        node.Skills.RemoveAll(ns => !wanted.Contains(ns.SkillId));
        for (var order = 0; order < wanted.Count; order++)
        {
            var skill = skills.FirstOrDefault(s => s.Id == wanted[order]);
            if (skill is null) continue; // removed from the store meanwhile
            var existing = node.Skills.FirstOrDefault(ns => ns.SkillId == skill.Id);
            if (existing is not null) existing.Order = order;
            else node.Skills.Add(new NodeSkill { AiNodeId = node.Id, SkillId = skill.Id, Skill = skill, Order = order });
        }
    }

    private static string SkillNote(AiNode node) => node.Skills.Count == 0 ? string.Empty : $", {node.Skills.Count} skill";

    private static NodeDto ToDto(AiNode n, IReadOnlyDictionary<Guid, string> names) => new(
        n.Id, n.Name, n.Role, n.Model, n.Temperature, n.MaxTokens, n.MaxConcurrency,
        n.Priority, n.SystemPrompt, n.IsActive, n.CreatedAt, n.UpdatedAt,
        n.Skills.OrderBy(s => s.Order).Where(s => s.Skill is not null)
            .Select(s => new NodeSkillDto(s.SkillId, s.Skill!.Name, s.Skill.Description, s.Skill.SkillSource?.Name ?? string.Empty))
            .ToList(),
        n.FallbackNodeId,
        n.FallbackNodeId is { } id && names.TryGetValue(id, out var name) ? name : null,
        n.ToolDelegations
            .OrderBy(d => PlannerTools.Delegable.IndexOf(d.Tool))
            .Select(d => new ToolDelegationDto(d.Tool, d.ToolsNodeId, d.ToolsNode?.Name ?? string.Empty, d.ToolsNode?.IsActive ?? false))
            .ToList());
}
