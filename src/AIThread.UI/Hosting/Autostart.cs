using System.Runtime.Versioning;
using AIThread.Common.Localization;
using Microsoft.Win32;

namespace AIThread.UI.Hosting;

// Starts the tray instance at Windows login through the per-user Run key: no admin rights, no scheduled task.
// (scripts/aithread.ps1 autostart-on uses a scheduled task instead; enable only one of the two.)
[SupportedOSPlatform("windows")]
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AIThread";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    // False when this is not a published exe (dotnet run / dotnet AIThread.UI.dll), which has no exe to launch.
    public static bool CanEnable => string.Equals(Path.GetFileNameWithoutExtension(AppPaths.ExePath), "dotnet", StringComparison.OrdinalIgnoreCase) is false;

    public static void Enable()
    {
        if (!CanEnable) throw new InvalidOperationException(Lang.T("Autostart hanya tersedia untuk AIThread.exe hasil publish.", "Autostart is only available for a published AIThread.exe."));
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{AppPaths.ExePath}\" tray");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public static void Toggle()
    {
        if (IsEnabled()) Disable();
        else Enable();
    }
}
