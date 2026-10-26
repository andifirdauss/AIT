using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class SetupProfileService
{
    private static string OnOff(bool value) => value ? Lang.T("aktif", "on") : Lang.T("mati", "off");
}
