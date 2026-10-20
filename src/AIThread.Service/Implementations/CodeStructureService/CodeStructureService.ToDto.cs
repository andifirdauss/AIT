using AIThread.Common.DTOs;
using AIThread.Service.Workspace.Structure;

namespace AIThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    private static StructureMemberDto ToDto(OutlineMember member) => new(member.Name, member.Kind, member.Line, member.Signature, EndLine: member.EndLine);
}
