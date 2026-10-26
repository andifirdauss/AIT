using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Implementations;

public sealed partial class SettingService
{
    private static void EnsureRange(int value, int min, int max, string name)
    {
        if (value < min || value > max) throw new AppValidationException(Lang.T($"{name} harus di antara {min} dan {max}.", $"{name} must be between {min} and {max}."));
    }
}
