using System.Text.Json;
using AIThread.Common.Options;
using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Databases;

public sealed partial class DatabaseBackupStore(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    IOptions<AIThreadOptions> options,
    ILogger<DatabaseBackupStore> logger) : IDatabaseBackupStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.DatabaseBackups.v1");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private string Folder => Path.Combine(
        string.IsNullOrWhiteSpace(options.Value.Databases.Directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread")
            : options.Value.Databases.Directory,
        "db-backups");

    // What the file holds (before compression and encryption).
    private sealed record BackupFile(string? Schema, string Table, List<SnapshotColumn> Columns, List<string?[]> Rows, DateTime TakenAt, string Checksum);
}
