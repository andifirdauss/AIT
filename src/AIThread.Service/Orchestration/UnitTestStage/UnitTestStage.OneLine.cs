using AIThread.Common.Localization;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    private static string OneLine(string? text) =>
        text is null ? Lang.T("(tanpa pesan)", "(no message)") : UnitTestText.Clip(text.ReplaceLineEndings(" ").Trim(), MaxOneLineChars);
}
