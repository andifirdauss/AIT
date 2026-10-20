namespace AiThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private static IEnumerable<List<Pending>> Batches(List<Pending> todo)
    {
        var batch = new List<Pending>();
        var size = 0;
        foreach (var pending in todo)
        {
            var length = Math.Min(pending.Body.Length, MaxBodyChars);
            if (batch.Count > 0 && (size + length > MaxBatchChars || batch.Count == MaxBatchFunctions))
            {
                yield return batch;
                batch = [];
                size = 0;
            }
            batch.Add(pending);
            size += length;
        }
        if (batch.Count > 0) yield return batch;
    }
}
