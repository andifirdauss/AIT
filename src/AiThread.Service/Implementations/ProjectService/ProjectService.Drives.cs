using AiThread.Common.DTOs;

namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    private static List<FolderEntryDto> Drives()
    {
        var drives = new List<FolderEntryDto>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Network or DriveType.Removable)) continue;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} {drive.VolumeLabel}";
                drives.Add(new FolderEntryDto(label, drive.RootDirectory.FullName, false, []));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Drive disappeared or is locked — skip it.
            }
        }
        return drives;
    }
}
