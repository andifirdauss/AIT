using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

// PackMessageIndex is the chat message holding the anchor of the thread whose pack was found.
public sealed record ResolvedSession(SessionContext Context, int? PackMessageIndex);

// Keeps a running summary ("context pack") of each CLI conversation, so every planner run starts from a small,
// current context instead of the whole chat. The pack written after a thread is stored on that thread, and threads are
// found in the chat through their ThreadAnchor line: a rewound or compacted chat simply resolves to an earlier pack.
public sealed partial class ContextKeeper(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    INodeService nodeService,
    INodeCaller caller,
    INodePromptBuilder prompts,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ContextKeeper> logger) : IContextKeeper
{
    // Every RebuildEvery-th turn the pack is rebuilt from the thread records rather than from the previous pack, so a
    // summary of summaries cannot drift for long.
    private const int RebuildEvery = 8;
    private const int MaxRebuildTurns = 15;

    // A turn's investigation as read_thread shows it; the pack input clips it further.
    private const int MaxInvestigationChars = 8_000;
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(60);
}
