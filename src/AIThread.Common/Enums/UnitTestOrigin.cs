namespace AIThread.Common.Enums;

// How a test came into the catalog: written in a thread, found in the repository by a scan, or written again from its
// scenario because neither the file nor a snapshot was there (trusted less until a developer confirms it).
public enum UnitTestOrigin
{
    Ait = 1,
    Scanned = 2,
    Regenerated = 3
}
