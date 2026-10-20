using AIThread.Common.DTOs;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    private static StructureMemberDto WithKey(StructureMemberDto dto, OutlineMember member, string path, string? typeName, string? container) =>
        FunctionSpans.IsScanned(member) ? dto with { Key = FunctionSpans.Key(path, FunctionSpans.SymbolOf(typeName, container, member.Name), member.Signature) } : dto;
}
