using AiThread.Common.Enums;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    // "calls: N" for the functions whose calls the tools node recorded (function_calls lists them).
    private async Task<IReadOnlyDictionary<OutlineMember, string>> CallNotesAsync(Guid projectId, string relative, OutlinedFile file,
        IReadOnlyList<OutlineType> types, IReadOnlyList<OutlineMember> functions, CancellationToken cancellationToken)
    {
        var keyed = types.SelectMany(t => t.Members.Select(m => (Member: m, Symbol: FunctionSpans.SymbolOf(t.Name, t.Container, m.Name))))
            .Concat(functions.Select(f => (Member: f, Symbol: f.Name)))
            .Where(m => FunctionSpans.IsScanned(m.Member))
            .Select(m => (m.Member, Key: FunctionSpans.Key(relative, m.Symbol, m.Member.Signature)))
            .ToList();
        var notes = new Dictionary<OutlineMember, string>(ReferenceEqualityComparer.Instance);
        if (keyed.Count == 0) return notes;
        var states = await store.StatesAsync(projectId, keyed.Select(k => k.Key).ToList(), cancellationToken);
        foreach (var (member, key) in keyed)
        {
            if (!states.TryGetValue(key, out var state) || state.Status != FunctionScanStatus.Done) continue;
            var changed = state.BodyHash != FunctionSpans.Hash(Body(file.Lines, new FunctionSpan(member.Name, member.Name, member.Signature, member.Line, member.EndLine)));
            notes[member] = $"calls: {state.Calls}{(changed ? ", changed since" : string.Empty)}";
        }
        return notes;
    }
}
