using AIThread.Common.Entities;
using AIThread.Service.Orchestration;
using AIThread.Service.Testing;
using ClosedXML.Excel;

namespace AIThread.Tests;

// Scenario sheets: AIT writes them from a tester's suggestion, a developer fills them in Excel, AIT reads them back.
public class TestScenarioTests
{
    private const string Key = "github.com/org/shop";

    private static readonly TestAccountSet Accounts = new(
        [new TestAccount { ProjectKey = Key, Username = "andi", Role = "Pengaju", Attributes = """{"NIK":"123"}""" }],
        new TestingSettings { ProjectKey = Key });

    private static TestingScenario Scenario() => ScenarioText.FromFollowUp(new TestFollowUp
    {
        Number = 1,
        Title = "Approval cuti 2 level",
        Why = "Alur approval harus selesai.",
        Fields = ["Jenis cuti", "Tanggal mulai"],
        Scenarios =
        [
            new TestScenario { Title = "Submit", Actor = "andi", Action = "submit", Target = "/Cuti/Create", Steps = ["Isi form dan kirim."], Expect = "Menunggu atasan" },
            new TestScenario { Title = "Approve", Actor = "Atasan", Action = "approve", Target = "/Approval", Steps = ["Approve pengajuan {{requestNo}}."], Expect = "Menunggu HR" }
        ]
    }, new TestPlan { Level = TestPlan.Ui, BaseUrl = "http://localhost:5000" }, Accounts, Key, "approval-cuti-2-level", Guid.NewGuid(), "andi");

    [Fact]
    public void A_suggestion_becomes_a_scenario_with_roles_for_its_actors()
    {
        var scenario = Scenario();

        var roles = ScenarioText.Roles(scenario);
        Assert.Equal([("Pengaju", "andi"), ("Atasan", (string?)null)], roles.Select(r => (r.Role, r.Username)));
        var steps = ScenarioText.Steps(scenario);
        Assert.Equal(("Pengaju", "submit", "Submit: Isi form dan kirim."), (steps[0].Role, steps[0].Action, steps[0].Instruction));
        Assert.Equal(["Jenis cuti", "Tanggal mulai"], ScenarioText.Fields(scenario));
        Assert.Equal(["Peran Atasan belum punya akun uji (sheet Akun)."], ScenarioText.Missing(scenario));
        Assert.Equal("approval-cuti-2-level-2", ScenarioText.Slug("Approval Cuti: 2 Level!", ["approval-cuti-2-level"]));
    }

    [Fact]
    public void A_filled_sheet_reads_back_with_accounts_values_and_new_steps()
    {
        var scenario = Scenario();
        var bytes = ScenarioSheet.Write(scenario, Accounts);

        // What a developer does in Excel: picks the approver, fills the form values, adds a step.
        using var filled = new MemoryStream();
        using (var workbook = new XLWorkbook(new MemoryStream(bytes)))
        {
            Assert.Equal(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("_AIT").Visibility);
            var accounts = workbook.Worksheet("Akun");
            Assert.Equal("123", accounts.Cell(2, 3).GetFormattedString());
            accounts.Cell(3, 2).Value = "budi";
            accounts.Cell(3, 3).Value = "456";
            accounts.Cell(4, 1).Value = "HR";
            accounts.Cell(4, 2).Value = "citra";
            var steps = workbook.Worksheet("Langkah");
            Assert.Equal("Jenis cuti", steps.Cell(1, 6).GetFormattedString());
            steps.Cell(2, 6).Value = "Tahunan";
            steps.Cell(2, 7).Value = "2026-11-02";
            steps.Cell(4, 2).Value = "HR";
            steps.Cell(4, 3).Value = "approve";
            steps.Cell(4, 5).Value = "Approve sebagai HR.";
            steps.Cell(4, 8).Value = "Disetujui";
            workbook.SaveAs(filled);
        }
        filled.Position = 0;

        var content = ScenarioSheet.Read(filled);

        Assert.Empty(content.Errors);
        Assert.Equal((scenario.Id, scenario.Version), (content.ScenarioId!.Value, content.Version!.Value));
        Assert.Equal([("Pengaju", "andi"), ("Atasan", "budi"), ("HR", "citra")], content.Accounts.Select(a => (a.Role, a.Username!)));
        Assert.Equal("456", content.Accounts[1].Attributes["NIK"]);
        Assert.Equal(3, content.Steps.Count);
        Assert.Equal(("Tahunan", "2026-11-02"), (content.Steps[0].Values["Jenis cuti"], content.Steps[0].Values["Tanggal mulai"]));
        Assert.Equal(("HR", "approve", "Disetujui"), (content.Steps[2].Role, content.Steps[2].Action, content.Steps[2].Expect));
        Assert.Equal(["Jenis cuti", "Tanggal mulai"], content.Fields);
    }

    [Fact]
    public void Rows_that_cannot_be_used_are_listed_with_their_row_number()
    {
        using var filled = new MemoryStream();
        using (var workbook = new XLWorkbook(new MemoryStream(ScenarioSheet.Write(Scenario(), Accounts))))
        {
            var steps = workbook.Worksheet("Langkah");
            steps.Cell(3, 2).Value = "Direktur";
            steps.Cell(3, 8).Value = string.Empty;
            workbook.SaveAs(filled);
        }
        filled.Position = 0;

        var errors = ScenarioSheet.Read(filled).Errors;

        Assert.Contains("Sheet Langkah baris 3: aktor \"Direktur\" tidak ada di sheet Akun.", errors);
        Assert.Contains("Sheet Langkah baris 3: kolom Hasil yang diharapkan kosong.", errors);
        Assert.Contains("Bukan file skenario AIT", ScenarioSheet.Read(new MemoryStream(NewWorkbook())).Errors.Single());
        Assert.Contains("bukan Excel", ScenarioSheet.Read(new MemoryStream("teks biasa"u8.ToArray())).Errors.Single());
    }

    [Theory]
    [InlineData("buat skenario test lanjutan 2", 2)]
    [InlineData("Buatkan skenario Excel untuk test lanjutan #1", 1)]
    [InlineData("buat skenario baru", null)]
    public void Sheet_requests_are_recognised(string reply, int? expected) => Assert.Equal(expected, TestScenarioReply.ParseCreate(reply));

    [Theory]
    [InlineData("jalankan skenario approval-cuti", "approval-cuti", false)]
    [InlineData("Jalankan skenario `approval-cuti.xlsx` setuju", "approval-cuti.xlsx", true)]
    public void Run_requests_are_recognised(string reply, string slug, bool agreed) => Assert.Equal((slug, agreed), TestScenarioReply.ParseRun(reply));

    private static byte[] NewWorkbook()
    {
        using var workbook = new XLWorkbook();
        workbook.Worksheets.Add("Sheet1").Cell(1, 1).Value = "x";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
