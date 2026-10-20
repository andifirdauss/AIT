using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Business;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static BusinessPageDto ToDto(BusinessPage p) => new(ToSummary(p), p.Content, BusinessMarkdown.ToHtml(p.Content));
}
