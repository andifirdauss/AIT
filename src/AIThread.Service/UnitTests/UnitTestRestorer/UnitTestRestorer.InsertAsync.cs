using AIThread.Common.Entities;
using AIThread.Service.Workspace;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // C#: the method moves from the snapshot of its file into the class of the file as it is now (CSharpMemberMover), so
    // no model has to copy it. False when that is not possible (another language, no snapshot, the class is gone).
    private static async Task<bool> InsertAsync(string root, UnitTestCase test, CancellationToken cancellationToken)
    {
        if (test.SourceSnapshot is null || !test.TestFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return false;
        var full = Full(root, test.TestFile);
        var state = await TextFileIo.ReadAsync(full, cancellationToken);
        if (!state.Exists) return false;

        var result = CSharpMemberMover.Move(TextFileIo.NormalizeNewLines(test.SourceSnapshot), state.Content, [test.MethodName]);
        if (!result.Success || result.Target is null || result.Moved.Count == 0) return false;
        await TextFileIo.WriteAsync(full, result.Target, state, cancellationToken);
        return true;
    }
}
