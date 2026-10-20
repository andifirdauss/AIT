using System.Text;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private static void AppendMember(StringBuilder builder, OutlineMember member, IReadOnlyDictionary<OutlineMember, string> calls)
    {
        // PHP and JavaScript signatures start with "function" already.
        var signature = member.Signature ?? member.Name;
        var kind = signature.StartsWith(member.Kind + " ", StringComparison.Ordinal) ? string.Empty : member.Kind + " ";
        builder.Append($"  {Range(member.Line, member.EndLine)} {kind}{signature}");
        if (calls.TryGetValue(member, out var note)) builder.Append($" [{note}]");
        builder.Append('\n');
    }
}
