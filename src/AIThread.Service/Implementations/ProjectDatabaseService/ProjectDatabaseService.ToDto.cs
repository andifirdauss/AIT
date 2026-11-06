using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    private static ProjectDatabaseDto ToDto(ProjectDatabase entity) => new(
        entity.Id, entity.ProjectId, entity.Name, entity.Provider, entity.Host, entity.Port, entity.Database, entity.Username,
        !string.IsNullOrEmpty(entity.PasswordProtected), entity.Options, entity.Environment, entity.MaskedColumns, entity.MaxRows, entity.AllowWrites,
        entity.Description, ToConnection(entity, null).Describe(), entity.CreatedAt, entity.UpdatedAt);
}
