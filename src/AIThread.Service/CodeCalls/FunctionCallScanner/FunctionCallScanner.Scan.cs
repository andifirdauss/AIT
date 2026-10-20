using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private static CodeFunctionScan Scan(string path, Pending pending, FunctionScanStatus status, string? model, int tokens, string? error, List<CodeFunctionCall> calls) => new()
    {
        Key = pending.Key,
        Path = path,
        Symbol = Truncate(pending.Span.Symbol, 300),
        Signature = pending.Span.Signature is { } signature ? Truncate(signature, 400) : null,
        StartLine = pending.Span.Line,
        EndLine = pending.Span.EndLine,
        BodyHash = pending.Hash,
        Status = status,
        Error = error,
        Model = model is null ? null : Truncate(model, 200),
        Tokens = tokens,
        Calls = calls
    };
}
