using System.Diagnostics;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    public async Task<NodeTestResultDto> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var node = await GetAsync(id, cancellationToken);
        if (node is null) return new NodeTestResultDto(false, null, 0, 0, Lang.T("Node tidak ditemukan.", "Node not found."));

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
}
