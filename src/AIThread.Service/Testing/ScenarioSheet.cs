using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using ClosedXML.Excel;

namespace AIThread.Service.Testing;

// What a developer filled in a scenario sheet: the scenario it belongs to (from the hidden _AIT tab), the accounts per
// role (Akun/Accounts tab, with their attributes), the steps (Langkah/Steps tab) and the form columns. Errors: rows that
// cannot be used, in the UI language with their row number.
public sealed record ScenarioSheetContent(
    Guid? ScenarioId,
    int? Version,
    IReadOnlyList<ScenarioSheetAccount> Accounts,
    IReadOnlyList<ScenarioStepDto> Steps,
    IReadOnlyList<string> Fields,
    IReadOnlyList<string> Errors);

public sealed record ScenarioSheetAccount(string Role, string? Username, IReadOnlyDictionary<string, string> Attributes);

// The Excel sheet of a scenario, written by AIT (never by a model) so it always reads back: Panduan (how to fill it),
// Akun (one row per role, with dropdown-free username cells and the project's attribute keys), Langkah (one row per step:
// role from Akun as a dropdown, action as a dropdown, one column per form field), and a very hidden _AIT tab with the
// scenario id and version. Cells the developer still has to fill are yellow. Tab and column names are written in the UI
// language and read in either language, so a sheet made by a teammate with the other language still reads back.
public static class ScenarioSheet
{
    public const string Format = "aithread-scenario";
    public const int FormatVersion = 1;

    private const string MetaTab = "_AIT";

    private static readonly Label GuideTab = new("Panduan", "Guide");
    private static readonly Label AccountsTab = new("Akun", "Accounts");
    private static readonly Label StepsTab = new("Langkah", "Steps");

    private static readonly Label RoleHeader = new("Peran", "Role");
    private static readonly Label UsernameHeader = new("Username", "Username");
    private static readonly Label NumberHeader = new("No", "No");
    private static readonly Label ActorHeader = new("Aktor (peran)", "Actor (role)");
    private static readonly Label ShortActorHeader = new("Aktor", "Actor");
    private static readonly Label ActionHeader = new("Aksi", "Action");
    private static readonly Label TargetHeader = new("Target", "Target");
    private static readonly Label InstructionHeader = new("Langkah", "Step");
    private static readonly Label ExpectHeader = new("Hasil yang diharapkan", "Expected result");

    // Rows the dropdowns cover: room for the developer to add steps and roles.
    private const int DropdownRows = 200;

    public static readonly string[] Actions = ["submit", "approve", "reject", "edit", "revisi", "batal", "lihat", "lainnya"];

    private static readonly XLColor Todo = XLColor.FromHtml("#FFF2A8");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E7ECF5");

    public static byte[] Write(TestingScenario scenario, TestAccountSet? accounts)
    {
        var steps = ScenarioText.Steps(scenario);
        var roles = ScenarioText.Roles(scenario);
        var fields = ScenarioText.Fields(scenario);
        var attributeKeys = accounts is null ? [] : TestAccountText.AttributeKeys(accounts.Accounts).Take(8).ToList();

        using var workbook = new XLWorkbook();
        WriteGuide(workbook.Worksheets.Add(GuideTab.Text), scenario);

        var accountSheet = workbook.Worksheets.Add(AccountsTab.Text);
        string[] accountHeaders = [RoleHeader.Text, UsernameHeader.Text, .. attributeKeys];
        Header(accountSheet, accountHeaders);
        for (var i = 0; i < roles.Count; i++)
        {
            var row = i + 2;
            var account = accounts?.Find(roles[i].Username);
            accountSheet.Cell(row, 1).Value = roles[i].Role;
            accountSheet.Cell(row, 2).Value = roles[i].Username ?? string.Empty;
            if (string.IsNullOrWhiteSpace(roles[i].Username)) accountSheet.Cell(row, 2).Style.Fill.SetBackgroundColor(Todo);
            var attributes = account is null ? new Dictionary<string, string>() : TestAccountText.ParseAttributes(account.Attributes);
            for (var k = 0; k < attributeKeys.Count; k++)
                if (attributes.TryGetValue(attributeKeys[k], out var value)) accountSheet.Cell(row, 3 + k).Value = value;
        }
        Widths(accountSheet, [18, 22, .. attributeKeys.Select(_ => 18)]);

        var stepSheet = workbook.Worksheets.Add(StepsTab.Text);
        string[] stepHeaders = [NumberHeader.Text, ActorHeader.Text, ActionHeader.Text, TargetHeader.Text, InstructionHeader.Text, .. fields, ExpectHeader.Text];
        Header(stepSheet, stepHeaders);
        for (var i = 0; i < steps.Count; i++)
        {
            var row = i + 2;
            var step = steps[i];
            stepSheet.Cell(row, 1).Value = i + 1;
            stepSheet.Cell(row, 2).Value = step.Role ?? string.Empty;
            stepSheet.Cell(row, 3).Value = step.Action;
            stepSheet.Cell(row, 4).Value = step.Target ?? string.Empty;
            stepSheet.Cell(row, 5).Value = step.Instruction;
            for (var f = 0; f < fields.Count; f++)
            {
                var cell = stepSheet.Cell(row, 6 + f);
                if (step.Values.TryGetValue(fields[f], out var value) && value.Length > 0) cell.Value = value;
                else cell.Style.Fill.SetBackgroundColor(Todo);
            }
            stepSheet.Cell(row, 6 + fields.Count).Value = step.Expect;
            if (string.IsNullOrWhiteSpace(step.Role)) stepSheet.Cell(row, 2).Style.Fill.SetBackgroundColor(Todo);
        }
        stepSheet.Range(2, 2, DropdownRows, 2).CreateDataValidation().List(accountSheet.Range(2, 1, DropdownRows, 1));
        stepSheet.Range(2, 3, DropdownRows, 3).CreateDataValidation().List($"\"{string.Join(",", Actions)}\"", true);
        Widths(stepSheet, [5, 18, 12, 22, 60, .. fields.Select(_ => 18), 40]);
        stepSheet.Column(5).Style.Alignment.WrapText = true;
        stepSheet.Column(6 + fields.Count).Style.Alignment.WrapText = true;

        var meta = workbook.Worksheets.Add(MetaTab);
        meta.Cell(1, 1).Value = "format";
        meta.Cell(1, 2).Value = Format;
        meta.Cell(2, 1).Value = "formatVersion";
        meta.Cell(2, 2).Value = FormatVersion;
        meta.Cell(3, 1).Value = "scenarioId";
        meta.Cell(3, 2).Value = scenario.Id.ToString();
        meta.Cell(4, 1).Value = "scenarioVersion";
        meta.Cell(4, 2).Value = scenario.Version;
        meta.Cell(5, 1).Value = "slug";
        meta.Cell(5, 2).Value = scenario.Slug;
        meta.Visibility = XLWorksheetVisibility.VeryHidden;

        stepSheet.SetTabActive();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static ScenarioSheetContent Read(Stream content)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(content);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(Lang.T($"File bukan Excel (.xlsx) yang bisa dibaca: {ex.Message}", $"The file is not a readable Excel (.xlsx) file: {ex.Message}"));
        }

        using (workbook)
        {
            if (!workbook.TryGetWorksheet(MetaTab, out var meta) || meta.Cell(1, 2).GetFormattedString() != Format)
                return Failed(Lang.T("Bukan file skenario AIT (sheet _AIT tidak ada). Unduh file skenario dari AIT, isi, lalu unggah lagi.",
                    "Not an AIT scenario file (the _AIT sheet is missing). Download the scenario file from AIT, fill it in, then upload it again."));
            var id = Guid.TryParse(meta.Cell(3, 2).GetFormattedString(), out var parsed) ? parsed : (Guid?)null;
            var version = int.TryParse(meta.Cell(4, 2).GetFormattedString(), out var v) ? v : (int?)null;
            if (Sheet(workbook, AccountsTab) is not { } accountSheet || Sheet(workbook, StepsTab) is not { } stepSheet)
                return Failed(Lang.T("Sheet Akun atau Langkah tidak ada di file.", "The Accounts or Steps sheet is missing from the file."));

            var errors = new List<string>();
            var accounts = ReadAccounts(accountSheet, errors);
            var (steps, fields) = ReadSteps(stepSheet, accounts, errors);
            return new ScenarioSheetContent(id, version, accounts, steps, fields, errors);
        }
    }

    private static List<ScenarioSheetAccount> ReadAccounts(IXLWorksheet sheet, List<string> errors)
    {
        var headers = Headers(sheet);
        var roleColumn = Column(headers, RoleHeader);
        var usernameColumn = Column(headers, UsernameHeader);
        if (roleColumn is null || usernameColumn is null)
        {
            errors.Add(Lang.T($"Sheet Akun: kolom \"{RoleHeader.Text}\" dan \"{UsernameHeader.Text}\" wajib ada di baris 1.",
                $"Accounts sheet: the \"{RoleHeader.Text}\" and \"{UsernameHeader.Text}\" columns are required in row 1."));
            return [];
        }

        var accounts = new List<ScenarioSheetAccount>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            var role = Text(sheet, row, roleColumn.Value);
            var username = Text(sheet, row, usernameColumn.Value);
            if (role.Length == 0 && username.Length == 0) continue;
            if (role.Length == 0)
            {
                errors.Add(Lang.T($"Sheet Akun baris {row}: peran kosong.", $"Accounts sheet row {row}: the role is empty."));
                continue;
            }
            if (accounts.Any(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(Lang.T($"Sheet Akun baris {row}: peran \"{role}\" sudah ada di baris lain.", $"Accounts sheet row {row}: the role \"{role}\" is already in another row."));
                continue;
            }
            if (username.Any(char.IsWhiteSpace))
            {
                errors.Add(Lang.T($"Sheet Akun baris {row}: username \"{username}\" tidak boleh berisi spasi.", $"Accounts sheet row {row}: the username \"{username}\" must not contain spaces."));
                continue;
            }
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (column, name) in headers)
                if (column != roleColumn && column != usernameColumn && Text(sheet, row, column) is { Length: > 0 } value) attributes[name] = value;
            accounts.Add(new ScenarioSheetAccount(role, username.Length == 0 ? null : username, attributes));
        }
        return accounts;
    }

    private static (List<ScenarioStepDto> Steps, List<string> Fields) ReadSteps(IXLWorksheet sheet, IReadOnlyList<ScenarioSheetAccount> accounts, List<string> errors)
    {
        var headers = Headers(sheet);
        var actor = Column(headers, ActorHeader) ?? Column(headers, ShortActorHeader);
        var action = Column(headers, ActionHeader);
        var target = Column(headers, TargetHeader);
        var instruction = Column(headers, InstructionHeader);
        var expect = Column(headers, ExpectHeader);
        if (instruction is null || expect is null)
        {
            errors.Add(Lang.T($"Sheet Langkah: kolom \"{InstructionHeader.Text}\" dan \"{ExpectHeader.Text}\" wajib ada di baris 1.",
                $"Steps sheet: the \"{InstructionHeader.Text}\" and \"{ExpectHeader.Text}\" columns are required in row 1."));
            return ([], []);
        }
        var known = new[] { Column(headers, NumberHeader), actor, action, target, instruction, expect };
        var fieldColumns = headers.Where(h => !known.Contains(h.Column)).ToList();

        var steps = new List<ScenarioStepDto>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            var text = Text(sheet, row, instruction.Value);
            var role = actor is null ? string.Empty : Text(sheet, row, actor.Value);
            var result = Text(sheet, row, expect.Value);
            if (text.Length == 0 && role.Length == 0 && result.Length == 0) continue;
            if (text.Length == 0) errors.Add(Lang.T($"Sheet Langkah baris {row}: kolom Langkah kosong.", $"Steps sheet row {row}: the Step column is empty."));
            if (result.Length == 0) errors.Add(Lang.T($"Sheet Langkah baris {row}: kolom Hasil yang diharapkan kosong.", $"Steps sheet row {row}: the Expected result column is empty."));
            if (role.Length > 0 && !accounts.Any(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase)))
                errors.Add(Lang.T($"Sheet Langkah baris {row}: aktor \"{role}\" tidak ada di sheet Akun.", $"Steps sheet row {row}: the actor \"{role}\" is not in the Accounts sheet."));
            if (text.Length == 0 || result.Length == 0) continue;
            if (steps.Count >= ScenarioText.MaxSteps)
            {
                errors.Add(Lang.T($"Sheet Langkah: maksimal {ScenarioText.MaxSteps} langkah; baris {row} dan seterusnya dilewati.",
                    $"Steps sheet: at most {ScenarioText.MaxSteps} steps; row {row} onward is skipped."));
                break;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (column, name) in fieldColumns)
                if (Text(sheet, row, column) is { Length: > 0 } value) values[name] = value;
            var matched = accounts.FirstOrDefault(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase));
            steps.Add(new ScenarioStepDto(steps.Count + 1, role.Length == 0 ? null : matched?.Role ?? role, action is null ? "lainnya" : Text(sheet, row, action.Value) is { Length: > 0 } a ? a : "lainnya",
                target is null ? null : Text(sheet, row, target.Value) is { Length: > 0 } t ? t : null, text, values, result));
        }
        return (steps, fieldColumns.Select(f => f.Name).Take(ScenarioText.MaxFields).ToList());
    }

    private static void WriteGuide(IXLWorksheet sheet, TestingScenario scenario)
    {
        string[] lines = Lang.IsEnglish
            ?
            [
                $"Test scenario: {scenario.Title}",
                scenario.Description ?? string.Empty,
                string.Empty,
                "How to fill it in",
                "1. Accounts sheet: fill in the Username of every role (yellow cells). A username that does not exist yet is added as a project test account when the file is uploaded. The other columns are account attributes (more can be added).",
                "2. Steps sheet: one row per step, in order. Actor = a role from the Accounts sheet (dropdown). Fill the form columns (yellow cells) with the data to test. Rows can be added, deleted or reordered.",
                $"3. Save the file, then reply \"run scenario {scenario.Slug}\" in the chat, or upload it in Project > Detail → Test scenarios.",
                string.Empty,
                "Warning",
                "The scenario runs against the running application: real submits and approvals create data and can send emails or notifications. Make sure the database and email of the dev environment do not reach real people.",
                $"Free text the Tester types starts with {ScenarioText.DataMarker} so test data is easy to find and clean up.",
                string.Empty,
                "Do not rename the sheets. The hidden _AIT sheet holds the scenario id."
            ]
            :
            [
                $"Skenario test: {scenario.Title}",
                scenario.Description ?? string.Empty,
                string.Empty,
                "Cara mengisi",
                "1. Sheet Akun: isi Username untuk setiap peran (sel kuning). Username yang belum ada otomatis ditambahkan sebagai akun uji project saat file diunggah. Kolom lain adalah atribut akun (boleh ditambah).",
                "2. Sheet Langkah: satu baris per langkah, berurutan. Aktor = peran dari sheet Akun (dropdown). Isi kolom form (sel kuning) dengan data yang mau dites. Baris boleh ditambah, dihapus, atau diurutkan ulang.",
                $"3. Simpan file, lalu balas \"jalankan skenario {scenario.Slug}\" di chat, atau unggah di Project > Detail → Skenario test.",
                string.Empty,
                "Peringatan",
                "Skenario dijalankan di aplikasi yang sedang berjalan: submit dan approve sungguhan membuat data dan bisa mengirim email atau notifikasi. Pastikan database dan email di lingkungan dev tidak mengarah ke orang asli.",
                $"Teks bebas yang diketik Tester diawali {ScenarioText.DataMarker} supaya data uji mudah dicari dan dibersihkan.",
                string.Empty,
                "Jangan ubah nama sheet. Sheet tersembunyi _AIT menyimpan id skenario."
            ];
        for (var i = 0; i < lines.Length; i++) sheet.Cell(i + 1, 1).Value = lines[i];
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(4, 1).Style.Font.Bold = true;
        sheet.Cell(9, 1).Style.Font.Bold = true;
        sheet.Column(1).Width = 120;
        sheet.Column(1).Style.Alignment.WrapText = true;
    }

    private static void Header(IXLWorksheet sheet, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.SetBackgroundColor(HeaderFill);
        }
        sheet.SheetView.FreezeRows(1);
    }

    private static void Widths(IXLWorksheet sheet, IReadOnlyList<int> widths)
    {
        for (var i = 0; i < widths.Count; i++) sheet.Column(i + 1).Width = widths[i];
    }

    private static List<(int Column, string Name)> Headers(IXLWorksheet sheet)
    {
        var last = sheet.Row(1).LastCellUsed()?.Address.ColumnNumber ?? 0;
        var headers = new List<(int, string)>();
        for (var column = 1; column <= last; column++)
            if (Text(sheet, 1, column) is { Length: > 0 } name) headers.Add((column, name));
        return headers;
    }

    private static int? Column(List<(int Column, string Name)> headers, Label label) =>
        headers.Where(h => label.Is(h.Name)).Select(h => (int?)h.Column).FirstOrDefault();

    private static IXLWorksheet? Sheet(XLWorkbook workbook, Label label) => workbook.Worksheets.FirstOrDefault(w => label.Is(w.Name));

    private static string Text(IXLWorksheet sheet, int row, int column) => sheet.Cell(row, column).GetFormattedString().Trim();

    private static ScenarioSheetContent Failed(string error) => new(null, null, [], [], [], [error]);

    // A tab or column name: written in the UI language, recognized in both.
    private sealed record Label(string Indonesian, string English)
    {
        public string Text => Lang.T(Indonesian, English);

        public bool Is(string? name) =>
            string.Equals(name, Indonesian, StringComparison.OrdinalIgnoreCase) || string.Equals(name, English, StringComparison.OrdinalIgnoreCase);
    }
}
