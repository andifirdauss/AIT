using AIThread.Common.Enums;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    private static CodeCallKind KindOf(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "function" => CodeCallKind.Function,
        "method" => CodeCallKind.Method,
        "http" => CodeCallKind.Http,
        "view" => CodeCallKind.View,
        "sql" => CodeCallKind.Sql,
        _ => CodeCallKind.Other
    };
}
