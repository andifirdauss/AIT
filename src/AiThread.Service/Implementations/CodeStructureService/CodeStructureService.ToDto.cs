using AiThread.Common.DTOs;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Service.Implementations;

public sealed partial class CodeStructureService
{
    private static StructureMemberDto ToDto(OutlineMember member) => new(member.Name, member.Kind, member.Line, member.Signature, EndLine: member.EndLine);
}
