using AIThread.Common.Entities;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    private static UnitTestRun Run(UnitTestSession session, UnitTestCase test, string phase, TestVerdict verdict, Guid? threadId) => new()
    {
        UnitTestCaseId = test.Id,
        ProjectKey = session.Project.Key,
        ThreadId = threadId,
        Phase = phase,
        Result = verdict.Result,
        DurationMs = verdict.DurationMs,
        Message = verdict.Message is null ? null : UnitTestText.Clip(verdict.Message, 2_000),
        SkipReason = verdict.SkipReason is null ? null : UnitTestText.Clip(verdict.SkipReason, 500),
        Machine = session.Machine,
        RunBy = session.Developer
    };
}
