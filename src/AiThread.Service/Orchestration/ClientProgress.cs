using System.Text;
using AiThread.Service.Models;

namespace AiThread.Service.Orchestration;

// What the client (Claude Code) sees: optional progress lines, then a separator, then the final answer.
// Also keeps the full transcript for non-streaming callers. Observer failures (client gone) never break a thread.
public sealed class ClientProgress(IThreadObserver observer, bool showProgress)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StringBuilder _transcript = new();
    private bool _anyProgress;
    private bool _finalStarted;

    public string Transcript
    {
        get
        {
            lock (_transcript) return _transcript.ToString();
        }
    }

    public async Task LineAsync(string line, CancellationToken cancellationToken)
    {
        if (!showProgress) return;
        await WriteAsync(line + "\n", isFinal: false, cancellationToken);
    }

    public Task FinalAsync(string delta, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(delta) ? Task.CompletedTask : WriteAsync(delta, isFinal: true, cancellationToken);

    private async Task WriteAsync(string text, bool isFinal, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (isFinal && !_finalStarted)
            {
                _finalStarted = true;
                if (_anyProgress) await EmitAsync("\n---\n\n", isFinal: true, cancellationToken);
            }
            if (!isFinal) _anyProgress = true;
            await EmitAsync(text, isFinal, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EmitAsync(string text, bool isFinal, CancellationToken cancellationToken)
    {
        lock (_transcript) _transcript.Append(text);
        try
        {
            if (isFinal) await observer.OnFinalDeltaAsync(text, cancellationToken);
            else await observer.OnProgressAsync(text, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The client stopped listening; the thread keeps its own record.
        }
    }
}
