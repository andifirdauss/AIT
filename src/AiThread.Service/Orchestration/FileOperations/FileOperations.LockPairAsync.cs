namespace AiThread.Service.Orchestration;

public sealed partial class FileOperations
{
    // Fixed lock order, so two operations on the same pair of files cannot deadlock.
    private async Task<IDisposable> LockPairAsync(string a, string b, CancellationToken cancellationToken)
    {
        var first = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a : b;
        var firstLease = await fileLocks.AcquireAsync(first, cancellationToken);
        try
        {
            var secondLease = await fileLocks.AcquireAsync(first == a ? b : a, cancellationToken);
            return new Leases(firstLease, secondLease);
        }
        catch
        {
            firstLease.Dispose();
            throw;
        }
    }
}
