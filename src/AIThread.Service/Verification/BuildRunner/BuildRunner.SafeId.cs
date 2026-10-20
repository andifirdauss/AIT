using System.Diagnostics;

namespace AIThread.Service.Verification;

public sealed partial class BuildRunner
{
    private static int? SafeId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
