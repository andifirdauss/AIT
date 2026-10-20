namespace AiThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    public static string Lead(string prefix, string name) => ClientProgress.Pad(prefix + name, LeadColumn);
}
