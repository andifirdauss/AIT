namespace AiThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    private static string OnOff(bool value) => value ? "aktif" : "mati";
}
