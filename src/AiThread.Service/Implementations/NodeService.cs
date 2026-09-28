using System.Diagnostics;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed class NodeService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    INineRouterService router,
    IEventBus eventBus,
    IActivityLogService activityLog) : INodeService
{
    public async Task<IReadOnlyList<NodeDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nodes = await db.Nodes.AsNoTracking().ToListAsync(cancellationToken);
        return nodes.OrderBy(n => n.Role).ThenBy(n => n.Priority).ThenBy(n => n.Name).Select(ToDto).ToList();
    }

    public async Task<NodeDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await db.Nodes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        return node is null ? null : ToDto(node);
    }

    public async Task<NodeDto> CreateAsync(SaveNodeRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = new AiNode();
        Apply(node, request);
        db.Nodes.Add(node);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "created" }));
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' ditambahkan ({node.Model}).");
        return ToDto(node);
    }

    public async Task<NodeDto?> UpdateAsync(Guid id, SaveNodeRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null) return null;

        Apply(node, request);
        node.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "updated" }));
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' diperbarui ({node.Model}).");
        return ToDto(node);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var node = await db.Nodes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (node is null) return false;

        db.Nodes.Remove(node);
        await db.SaveChangesAsync(cancellationToken);

        eventBus.Publish(AppEvent.Create(AppEventTypes.NodeChanged, new { node.Id, action = "deleted" }));
        activityLog.Info("node", $"Node {node.Role} '{node.Name}' dihapus.");
        return true;
    }

    public async Task<IReadOnlyList<AiNode>> GetActiveAsync(NodeRole role, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Nodes.AsNoTracking()
            .Where(n => n.Role == role && n.IsActive)
            .OrderBy(n => n.Priority).ThenBy(n => n.Name)
            .ToListAsync(cancellationToken);
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
        if (planners > 1) issues.Add("Lebih dari satu Planner aktif — yang dipakai adalah prioritas terkecil.");
        if (wrappers > 1) issues.Add("Lebih dari satu Wrapper aktif — yang dipakai adalah prioritas terkecil.");

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
    }

    private static NodeDto ToDto(AiNode n) => new(
        n.Id, n.Name, n.Role, n.Model, n.Temperature, n.MaxTokens, n.MaxConcurrency,
        n.Priority, n.SystemPrompt, n.IsActive, n.CreatedAt, n.UpdatedAt);
}
