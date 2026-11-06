using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Data.Memory;
using Microsoft.AspNetCore.DataProtection;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class ProjectDatabaseService
{
    // Validates the request and copies it onto the entity. A null password keeps the stored one, "" clears it.
    private void Apply(ProjectDatabase entity, SaveProjectDatabaseRequest request)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (!NamePattern().IsMatch(name))
            throw new AppValidationException(Lang.T("Nama database wajib diisi: huruf, angka, '_', '.', atau '-' (maks 100).", "The database name is required: letters, digits, '_', '.' or '-' (at most 100)."));
        if (request.Provider is not (MemoryStoreProvider.SqlServer or MemoryStoreProvider.PostgreSql or MemoryStoreProvider.MySql or MemoryStoreProvider.Oracle or MemoryStoreProvider.Sqlite))
            throw new AppValidationException(Lang.T("Pilih jenis database: SQL Server, PostgreSQL, MySQL, Oracle, atau SQLite.", "Choose a database type: SQL Server, PostgreSQL, MySQL, Oracle or SQLite."));
        if (!Enum.IsDefined(request.Environment))
            throw new AppValidationException(Lang.T("Pilih environment: Dev, Staging, atau Prod.", "Choose an environment: Dev, Staging or Prod."));
        if (string.IsNullOrWhiteSpace(request.Database))
            throw new AppValidationException(request.Provider == MemoryStoreProvider.Sqlite
                ? Lang.T("Isi path file SQLite.", "Fill in the SQLite file path.")
                : Lang.T("Isi nama database.", "Fill in the database name."));
        if (request.Provider != MemoryStoreProvider.Sqlite && string.IsNullOrWhiteSpace(request.Host))
            throw new AppValidationException(Lang.T("Isi host database.", "Fill in the database host."));
        if (request.Port is < 1 or > 65535)
            throw new AppValidationException(Lang.T("Port harus 1–65535.", "The port must be 1–65535."));
        var maxRows = request.MaxRows ?? entity.MaxRows;
        if (maxRows is < 1 or > MaxRowsLimit)
            throw new AppValidationException(Lang.T($"Maks baris per query harus 1–{MaxRowsLimit}.", $"Max rows per query must be 1–{MaxRowsLimit}."));

        entity.Name = name;
        entity.Provider = request.Provider;
        entity.Host = request.Provider == MemoryStoreProvider.Sqlite ? null : request.Host?.Trim();
        entity.Port = request.Provider == MemoryStoreProvider.Sqlite ? null : request.Port;
        entity.Database = request.Database.Trim();
        entity.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        if (request.Password is not null) entity.PasswordProtected = request.Password.Length == 0 ? null : _protector.Protect(request.Password);
        entity.Options = string.IsNullOrWhiteSpace(request.Options) ? null : request.Options.Trim();
        entity.Environment = request.Environment;
        entity.MaskedColumns = string.IsNullOrWhiteSpace(request.MaskedColumns) ? null : string.Join(", ", Databases.SensitiveColumns.Parse(request.MaskedColumns));
        entity.MaxRows = maxRows;
        entity.AllowWrites = request.AllowWrites;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        // An invalid option keyword is reported now, not on the first query.
        try
        {
            ToConnection(entity, null).BuildConnectionString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new AppValidationException(Lang.T($"Opsi koneksi tidak valid: {ex.Message}", $"Invalid connection options: {ex.Message}"));
        }
    }
}
