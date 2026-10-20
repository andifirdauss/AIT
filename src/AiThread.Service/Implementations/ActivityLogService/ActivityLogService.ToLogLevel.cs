using AiThread.Common.Enums;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed partial class ActivityLogService
{
    private static LogLevel ToLogLevel(ActivityLevel level) => level switch
    {
        ActivityLevel.Debug => LogLevel.Debug,
        ActivityLevel.Warning => LogLevel.Warning,
        ActivityLevel.Error => LogLevel.Error,
        _ => LogLevel.Information
    };
}
