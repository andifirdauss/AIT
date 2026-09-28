using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using AiThread.Common.DTOs;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

// Server-Sent Events feed for the UI. The event name is the AppEvent type (log, router.status, task.updated, ...).
//   /api/stream?types=log,router,thread           dashboard (prefix filter)
//   /api/stream?threadId={id}                     everything about one thread
[ApiController]
[Route("api/stream")]
public class StreamController(IEventBus eventBus, INineRouterService router) : ControllerBase
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

    [HttpGet]
    public IResult Get([FromQuery] Guid? threadId, [FromQuery] string? types)
    {
        var prefixes = string.IsNullOrWhiteSpace(types)
            ? null
            : types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return TypedResults.ServerSentEvents(StreamAsync(threadId, prefixes, HttpContext.RequestAborted));
    }

    private async IAsyncEnumerable<SseItem<object?>> StreamAsync(
        Guid? threadId,
        string[]? prefixes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var subscription = eventBus.Subscribe(evt => Matches(evt, threadId, prefixes));

        // Snapshot so a freshly opened dashboard does not wait for the next monitor tick.
        if (threadId is null && router.LastStatus is { } status && Matches(AppEvent.Create(AppEventTypes.RouterStatus, null), null, prefixes))
            yield return Item(AppEvent.Create(AppEventTypes.RouterStatus, status));

        Task<bool>? pending = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            pending ??= subscription.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var winner = await Task.WhenAny(pending, Task.Delay(HeartbeatInterval, cancellationToken));
            if (cancellationToken.IsCancellationRequested) yield break;

            if (winner != pending)
            {
                yield return Item(AppEvent.Create(AppEventTypes.Ping, null));
                continue;
            }

            if (!pending.IsCompletedSuccessfully || !pending.Result) yield break;
            pending = null;

            while (subscription.Reader.TryRead(out var evt)) yield return Item(evt);
        }
    }

    private static bool Matches(AppEvent evt, Guid? threadId, string[]? prefixes)
    {
        if (threadId is not null && evt.ThreadId != threadId) return false;
        return prefixes is null || prefixes.Any(p => evt.Type.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static SseItem<object?> Item(AppEvent evt) =>
        new(new { data = evt.Data, threadId = evt.ThreadId, timestamp = evt.Timestamp }, evt.Type);
}
