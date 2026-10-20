using AiThread.Service.Interfaces;

namespace AiThread.Service.Orchestration;

// Delete, move and extract tasks need no model: AiThread does them itself, under the same file locks the executors use,
// and records them in the thread's FileJournal so ThreadRollback can bring the files back. A move keeps the exact bytes;
// changing the content at the new path is a separate edit task that depends on the move. An extract moves C# members
// verbatim into another file (CSharpMemberMover).
public sealed partial class FileOperations(IFileLockProvider fileLocks) : IFileOperations
{
    private sealed class Leases(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            second.Dispose();
            first.Dispose();
        }
    }
}
