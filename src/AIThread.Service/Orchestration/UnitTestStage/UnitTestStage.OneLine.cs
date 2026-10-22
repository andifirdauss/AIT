using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    private static string OneLine(string? text) =>
        text is null ? "(tanpa pesan)" : UnitTestText.Clip(text.ReplaceLineEndings(" ").Trim(), MaxOneLineChars);
}
