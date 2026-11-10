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
    private const string ValueName = "AIT";
    // Value name used before v1.12.x; still recognised and cleaned up.
    private const string LegacyValueName = "AIThread";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string || key?.GetValue(LegacyValueName) is string;
    }

    // False when this is not a published exe (dotnet run / dotnet AIThread.UI.dll), which has no exe to launch.
    public static bool CanEnable => string.Equals(Path.GetFileNameWithoutExtension(AppPaths.ExePath), "dotnet", StringComparison.OrdinalIgnoreCase) is false;

    public static void Enable()
    {
        if (!CanEnable) throw new InvalidOperationException(Lang.T("Autostart hanya tersedia untuk AIThread.exe hasil publish.", "Autostart is only available for a published AIThread.exe."));
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, Command(AppPaths.ExePath, ConhostPath()));
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    // AIThread.exe is a console program (chat, the menu), so a plain start at login opens a console window until the tray
    // releases it. A headless console host gives it a console nobody sees. conhost: null when this Windows has no such host.
    public static string Command(string exePath, string? conhost) =>
        conhost is null ? $"\"{exePath}\" tray" : $"\"{conhost}\" --headless \"{exePath}\" tray";

    // An entry of this same exe written before the headless start. An entry of another install is left alone.
    public static bool NeedsUpgrade(string? existing, string exePath, string? conhost) =>
        existing is not null && existing.Contains(exePath, StringComparison.OrdinalIgnoreCase) && !string.Equals(existing, Command(exePath, conhost), StringComparison.OrdinalIgnoreCase);

    // Run by the tray: the developer does not have to switch autostart off and on to lose the console window. An entry
    // that cannot be rewritten only keeps the window, so it never stops the tray.
    public static void Upgrade()
    {
        if (!CanEnable) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            var existing = (key?.GetValue(ValueName) ?? key?.GetValue(LegacyValueName)) as string;
            if (NeedsUpgrade(existing, AppPaths.ExePath, ConhostPath())) Enable();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Console.Error.WriteLine(Lang.T($"Entri autostart tidak bisa diperbarui: {ex.Message}", $"The autostart entry could not be updated: {ex.Message}"));
        }
    }

    // --headless needs Windows 10 1809 or newer.
    private static string? ConhostPath()
    {
        var conhost = Path.Combine(Environment.SystemDirectory, "conhost.exe");
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) && File.Exists(conhost) ? conhost : null;
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        key?.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }

    public static void Toggle()
    {
        if (IsEnabled()) Disable();
        else Enable();
    }
}
