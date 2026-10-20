namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    private static string PairKey(string a, string b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0 ? $"{a}|{b}" : $"{b}|{a}";
}
