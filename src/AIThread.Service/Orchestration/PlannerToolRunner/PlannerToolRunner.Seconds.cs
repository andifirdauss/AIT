using System.Globalization;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private static string Seconds(TimeSpan elapsed) => elapsed.TotalSeconds < 60
        ? elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s"
        : $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";
}
