using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The background processes the planner started for this thread (start_process) stop.
    private void StopBackgroundProcesses(ThreadRecorder recorder)
    {
        var stopped = backgroundProcesses.StopThread(recorder.Thread.Id);
        if (stopped > 0)
            recorder.Log(ActivityLevel.Info, Lang.T($"{stopped} proses latar belakang dari planner dihentikan.", $"{stopped} background process(es) of the planner stopped."));
    }
}
