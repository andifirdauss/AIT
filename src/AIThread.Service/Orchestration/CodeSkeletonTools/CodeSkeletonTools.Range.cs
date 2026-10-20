namespace AIThread.Service.Orchestration;

public sealed partial class CodeSkeletonTools
{
    private static string Range(int line, int endLine) => endLine > line ? $"{line}-{endLine}" : line.ToString();
}
