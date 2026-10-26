using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Data.Memory;
using AIThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryStorageService
{
    // Validates the form of one level and resolves the password to keep.
    private (MemoryStoreSetting Setting, MemoryStoreConnection Connection) Normalize(SaveMemoryStoreRequest request, MemoryLevel level, MemoryStoreSetting saved)
    {
        var levelName = level switch { MemoryLevel.Short => "Short memory", MemoryLevel.Business => "Business memory", _ => "Long memory" };
        var provider = request.Provider;
        if (!Enum.IsDefined(provider)) throw new AppValidationException(Lang.T($"{levelName}: provider tidak valid.", $"{levelName}: invalid provider."));
        if (provider == MemoryStoreProvider.Redis && level != MemoryLevel.Short)
            throw new AppValidationException(Lang.T($"{levelName} tidak bisa disimpan di Redis — pilih database.", $"{levelName} cannot be stored in Redis — choose a database."));

        if (provider == MemoryStoreProvider.Default) return (new MemoryStoreSetting(), MemoryStoreConnection.Default);

        var host = Clean(request.Host);
        var database = Clean(request.Database);
        var username = Clean(request.Username);
        var options = Clean(request.Options);
        string? password = null;

        if (provider == MemoryStoreProvider.Sqlite)
        {
            host = null;
            username = null;
            database = Path.GetFullPath(Environment.ExpandEnvironmentVariables(database ?? DefaultSqlitePath));
        }
        else
        {
            if (host is null) throw new AppValidationException(Lang.T($"{levelName}: host wajib diisi.", $"{levelName}: the host is required."));
            if (request.Port is { } p && (p < 1 || p > 65535)) throw new AppValidationException(Lang.T($"{levelName}: port harus di antara 1 dan 65535.", $"{levelName}: the port must be between 1 and 65535."));

            if (provider == MemoryStoreProvider.Redis)
            {
                if (database is not null && (!int.TryParse(database, out var index) || index < 0))
                    throw new AppValidationException(Lang.T($"{levelName}: database Redis berupa angka (0, 1, 2, …).", $"{levelName}: a Redis database is a number (0, 1, 2, …)."));
            }
            else if (database is null)
            {
                throw new AppValidationException(provider == MemoryStoreProvider.Oracle
                    ? Lang.T($"{levelName}: service name Oracle wajib diisi (mis. FREEPDB1).", $"{levelName}: the Oracle service name is required (e.g. FREEPDB1).")
                    : Lang.T($"{levelName}: nama database wajib diisi.", $"{levelName}: the database name is required."));
            }

            if (username is null && provider is MemoryStoreProvider.PostgreSql or MemoryStoreProvider.MySql or MemoryStoreProvider.Oracle)
                throw new AppValidationException(Lang.T($"{levelName}: username wajib diisi.", $"{levelName}: the username is required."));

            password = request.ClearPassword ? null
                : !string.IsNullOrEmpty(request.Password) ? request.Password
                : Unprotect(saved.PasswordProtected);
        }

        EnsureLength(host, 255, $"{levelName}: host");
        EnsureLength(database, 500, $"{levelName}: database");
        EnsureLength(username, 200, $"{levelName}: username");
        EnsureLength(options, 1000, Lang.T($"{levelName}: opsi tambahan", $"{levelName}: extra options"));

        var port = provider == MemoryStoreProvider.Sqlite ? null : request.Port;
        var connection = new MemoryStoreConnection(provider, host, port, database, username, password, options);
        try
        {
            // Builds the connection string once so an unknown option keyword is reported now.
            if (provider == MemoryStoreProvider.Redis) connection.BuildRedisConfiguration();
            else connection.BuildConnectionString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new AppValidationException(Lang.T($"{levelName}: opsi tambahan tidak valid — {ex.Message}", $"{levelName}: invalid extra options — {ex.Message}"));
        }

        var setting = new MemoryStoreSetting
        {
            Provider = provider,
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            PasswordProtected = password is null ? null : _protector.Protect(password),
            Options = options
        };
        return (setting, connection);
    }
}
