using AIThread.Common.Localization;
using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    // What a change_database backup covers, for the developer.
    private static string ScopeText(BackupScope scope) => scope.Kind switch
    {
        BackupScopeKind.Rows => Lang.T("hanya baris yang cocok dengan WHERE", "only the rows the WHERE matches"),
        BackupScopeKind.Insert => Lang.T("INSERT: kunci baris baru dicatat saat dijalankan", "INSERT: the keys of the new rows are recorded when it runs"),
        _ => Lang.T("seluruh tabel", "the whole table")
    };
}
