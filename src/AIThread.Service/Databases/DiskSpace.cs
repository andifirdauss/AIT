using System.Globalization;

namespace AIThread.Service.Databases;

// The only limit on a backup is the disk: AIT stops before it would leave less than the reserve
// (ProjectDatabaseOptions.BackupFreeSpaceReserveMb) free, and the half-written file is deleted by the caller.
public static class DiskSpace
{
    // Free bytes on the drive of path; null when it cannot be told (a network share, for example).
    public static long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Throws when the drive would keep less than reserve free after needed more bytes are written.
    public static void Ensure(string path, long needed, long reserve, string what)
    {
        if (FreeBytes(path) is not { } free) return;
        if (free - Math.Max(0, needed) >= reserve) return;
        var drive = Path.GetPathRoot(Path.GetFullPath(path));
        throw new DatabaseToolException(
            $"not enough disk space for {what} on {drive}: {Format(free)} free, about {Format(Math.Max(0, needed))} still needed, and AIT keeps {Format(reserve)} free " +
            "(AIThread:Databases:BackupFreeSpaceReserveMb); free up space or point AIThread:Databases:Directory at another disk, then try again");
    }

    public static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / 1024d / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / 1024d / 1024 / 1024).ToString("0.##", CultureInfo.InvariantCulture) + " GB"
    };
}
