using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class BusinessMemoryService
{
    private static BusinessPageSummaryDto ToSummary(BusinessPage p) => new(
        p.Id, p.ProjectKey, p.ProjectName, p.Module, p.Title, p.Slug, p.Summary, p.Labels, p.Version, p.AutoUpdate,
        p.CreatedAt, p.UpdatedAt, p.UpdatedBy, p.LastEditKind);
}
