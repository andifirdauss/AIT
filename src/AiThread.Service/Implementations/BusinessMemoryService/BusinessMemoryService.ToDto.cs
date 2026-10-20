using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Service.Business;

namespace AiThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static BusinessPageDto ToDto(BusinessPage p) => new(ToSummary(p), p.Content, BusinessMarkdown.ToHtml(p.Content));
}
