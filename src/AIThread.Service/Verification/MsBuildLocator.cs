using System.ComponentModel;
using System.Diagnostics;

namespace AIThread.Service.Verification;

// Finds MSBuild.exe of Visual Studio or the Build Tools. .NET Framework projects in the old (non-SDK) format import
// targets that only ship with Visual Studio (Microsoft.WebApplication.targets and friends), so dotnet build cannot
// build them. vswhere knows every installation, also outside Program Files; without it the standard folders are
// searched. The newest MSBuild wins. Looked up once per process.
public static class MsBuildLocator
{
    private static readonly TimeSpan VswhereTimeout = TimeSpan.FromSeconds(10);
    private static readonly Lazy<string?> Cached = new(Locate);

    public static string? Find() => Cached.Value;

    private static string? Locate()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var candidates = FromVswhere().Concat(FromFolders())
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return candidates.OrderByDescending(FileVersion).FirstOrDefault();
    }

    private static IEnumerable<string> FromVswhere()
    {
        var vswhere = Path.Combine(ProgramFilesX86(), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere)) return [];

        var startInfo = new ProcessStartInfo(vswhere)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-latest", "-prerelease", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe", "-nologo" })
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return [];
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(VswhereTimeout))
            {
                process.Kill(entireProcessTree: true);
                return [];
            }
            return output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                // amd64\ and arm64\ hold the same MSBuild for other host architectures.
                .Where(p => string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "Bin", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return [];
        }
    }

    // <Program Files>\Microsoft Visual Studio\<version>\<edition>\MSBuild\<Current|15.0>\Bin\MSBuild.exe
    private static IEnumerable<string> FromFolders()
    {
        foreach (var programFiles in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProgramFilesX86() }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var visualStudio = Path.Combine(programFiles, "Microsoft Visual Studio");
            foreach (var edition in SubDirectories(visualStudio).SelectMany(SubDirectories))
            foreach (var version in SubDirectories(Path.Combine(edition, "MSBuild")))
                yield return Path.Combine(version, "Bin", "MSBuild.exe");
        }
    }

    private static IEnumerable<string> SubDirectories(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetDirectories(directory) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static Version FileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return new Version(0, 0);
        }
    }

    private static string ProgramFilesX86()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return string.IsNullOrEmpty(path) ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) : path;
    }
}
