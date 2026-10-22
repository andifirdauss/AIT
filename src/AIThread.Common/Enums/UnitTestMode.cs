namespace AIThread.Common.Enums;

// Which tests of the catalog a thread runs before and after its change: none, the ones related to the files it touches,
// or every active test.
public enum UnitTestMode
{
    Off = 0,
    Relevant = 1,
    All = 2
}
