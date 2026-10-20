using AIThread.UI.Hosting;

// The exe menu, tray icon and autostart are Win32 and registry based; LocalDB already ties AIThread to Windows.
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("AIT hanya berjalan di Windows.");
    return 1;
}

return await Cli.RunAsync(args);
