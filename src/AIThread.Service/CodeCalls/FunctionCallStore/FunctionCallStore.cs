using AIThread.Common.Enums;
using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.CodeCalls;

public sealed record FunctionScanState(FunctionScanStatus Status, string BodyHash, int Calls, DateTime? UpdatedAt);

// A function that calls another one, at Line of Path.
public sealed record FunctionCaller(string Path, string Symbol, int Line);

// The scanned functions of a project (CodeFunctionScans) and their calls, in the main database of this machine.
public sealed partial class FunctionCallStore(IDbContextFactory<AIThreadDbContext> dbFactory) : IFunctionCallStore
{
}
