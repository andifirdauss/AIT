using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using ClosedXML.Excel;

namespace AIThread.Service.Testing;

// What a developer filled in a scenario sheet: the scenario it belongs to (from the hidden _AIT tab), the accounts per
// role (Akun tab, with their attributes), the steps (Langkah tab) and the form columns. Errors: rows that cannot be used,
// in Indonesian with their row number.
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
// scenario id and version. Cells the developer still has to fill are yellow.
public static class ScenarioSheet
{
    public const string Format = "aithread-scenario";
    public const int FormatVersion = 1;

    private const string GuideTab = "Panduan";
    private const string AccountsTab = "Akun";
    private const string StepsTab = "Langkah";
    private const string MetaTab = "_AIT";

    private const string RoleHeader = "Peran";
    private const string UsernameHeader = "Username";
    private const string NumberHeader = "No";
    private const string ActorHeader = "Aktor (peran)";
    private const string ActionHeader = "Aksi";
    private const string TargetHeader = "Target";
    private const string InstructionHeader = "Langkah";
    private const string ExpectHeader = "Hasil yang diharapkan";

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
        WriteGuide(workbook.Worksheets.Add(GuideTab), scenario);

        var accountSheet = workbook.Worksheets.Add(AccountsTab);
        string[] accountHeaders = [RoleHeader, UsernameHeader, .. attributeKeys];
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

        var stepSheet = workbook.Worksheets.Add(StepsTab);
        string[] stepHeaders = [NumberHeader, ActorHeader, ActionHeader, TargetHeader, InstructionHeader, .. fields, ExpectHeader];
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

        workbook.Worksheet(StepsTab).SetTabActive();
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
            return Failed($"File bukan Excel (.xlsx) yang bisa dibaca: {ex.Message}");
        }

        using (workbook)
        {
            if (!workbook.TryGetWorksheet(MetaTab, out var meta) || meta.Cell(1, 2).GetFormattedString() != Format)
                return Failed("Bukan file skenario AIT (sheet _AIT tidak ada). Unduh file skenario dari AIT, isi, lalu unggah lagi.");
            var id = Guid.TryParse(meta.Cell(3, 2).GetFormattedString(), out var parsed) ? parsed : (Guid?)null;
            var version = int.TryParse(meta.Cell(4, 2).GetFormattedString(), out var v) ? v : (int?)null;
            if (!workbook.TryGetWorksheet(AccountsTab, out var accountSheet) || !workbook.TryGetWorksheet(StepsTab, out var stepSheet))
                return Failed("Sheet Akun atau Langkah tidak ada di file.");

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
            errors.Add($"Sheet Akun: kolom \"{RoleHeader}\" dan \"{UsernameHeader}\" wajib ada di baris 1.");
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
                errors.Add($"Sheet Akun baris {row}: peran kosong.");
                continue;
            }
            if (accounts.Any(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Sheet Akun baris {row}: peran \"{role}\" sudah ada di baris lain.");
                continue;
            }
            if (username.Any(char.IsWhiteSpace))
            {
                errors.Add($"Sheet Akun baris {row}: username \"{username}\" tidak boleh berisi spasi.");
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
        var actor = Column(headers, ActorHeader) ?? Column(headers, "Aktor");
        var action = Column(headers, ActionHeader);
        var target = Column(headers, TargetHeader);
        var instruction = Column(headers, InstructionHeader);
        var expect = Column(headers, ExpectHeader);
        if (instruction is null || expect is null)
        {
            errors.Add($"Sheet Langkah: kolom \"{InstructionHeader}\" dan \"{ExpectHeader}\" wajib ada di baris 1.");
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
            if (text.Length == 0) errors.Add($"Sheet Langkah baris {row}: kolom Langkah kosong.");
            if (result.Length == 0) errors.Add($"Sheet Langkah baris {row}: kolom Hasil yang diharapkan kosong.");
            if (role.Length > 0 && !accounts.Any(a => string.Equals(a.Role, role, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Sheet Langkah baris {row}: aktor \"{role}\" tidak ada di sheet Akun.");
            if (text.Length == 0 || result.Length == 0) continue;
            if (steps.Count >= ScenarioText.MaxSteps)
            {
                errors.Add($"Sheet Langkah: maksimal {ScenarioText.MaxSteps} langkah; baris {row} dan seterusnya dilewati.");
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
        string[] lines =
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

    private static int? Column(List<(int Column, string Name)> headers, string name) =>
        headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Select(h => (int?)h.Column).FirstOrDefault();

    private static string Text(IXLWorksheet sheet, int row, int column) => sheet.Cell(row, column).GetFormattedString().Trim();

    private static ScenarioSheetContent Failed(string error) => new(null, null, [], [], [], [error]);
}
