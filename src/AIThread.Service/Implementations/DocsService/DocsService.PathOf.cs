using AIThread.Common.Localization;

namespace AIThread.Service.Implementations;

public sealed partial class DocsService
{
    // The file of a page in the UI language: docs/en/<name> when English is chosen and the page is translated, else the
    // Indonesian original. Both use the same file names, so slugs and links between pages stay the same.
    private string PathOf(string fileName)
    {
        if (Lang.IsEnglish && Path.Combine(DirectoryPath!, EnglishFolder, fileName) is var english && File.Exists(english)) return english;
        return Path.Combine(DirectoryPath!, fileName);
    }
}
