using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class NodeService
{
    // Whether any project keeps active unit tests in the catalog; false when the store cannot be read.
    private async Task<bool> HasUnitTestsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await unitTests.CreateUnitTestsAsync(cancellationToken);
            return await db.UnitTestCases.AnyAsync(t => t.State == UnitTestState.Active, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
