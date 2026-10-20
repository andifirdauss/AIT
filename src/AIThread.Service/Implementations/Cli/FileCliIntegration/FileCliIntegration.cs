using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations.Cli;

// A CLI whose AIThread entry lives in one config file per scope: Info.GlobalPath, and Info.ProjectFile inside a
// project folder (null = global only). Subclasses only transform the file's text; reading, the one-time
// "*.aithread-backup", project lookup and deleting a file that held nothing but AIThread's entry are shared.
public abstract partial class FileCliIntegration(ISettingService settingService, IProjectService? projectService = null) : ICliIntegration
{
    private const string BackupSuffix = ".aithread-backup";

    public abstract CliInfoDto Info { get; }

    protected static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

// What a CLI's config points at: BaseUrl already in the CLI's own form (see ConfigBaseUrl). ProjectRoot is set for
// a project-scope file.
public sealed record CliEntry(string BaseUrl, string ApiKey, string? ProjectRoot);
