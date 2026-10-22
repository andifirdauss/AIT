using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AIThread.Common.Enums;

namespace AIThread.Service.Verification;

// Reads what a test runner reported: the TRX files of dotnet test / vstest.console, JUnit XML (pytest, PHPUnit, Maven
// surefire, Gradle), the Jest-style JSON of jest and vitest, Mocha's JSON, the JSON lines of go test -json and the text
// of cargo test. Never throws on a broken report: the error comes back instead and the run counts as without per-test
// results.
public static partial class TestResultParser
{
    private const int MaxMessageChars = 1_500;

    // output: what the runner printed (go and cargo report there). startedUtc: report files older than the run are
    // left alone (Maven and Gradle keep earlier reports in the same folder).
    public static (IReadOnlyList<TestCaseOutcome> Outcomes, string? Error) Parse(TestCommand command, string output, DateTime startedUtc)
    {
        try
        {
            return command.Suite.Format switch
            {
                TestReportFormat.Trx => FromFiles(command.ReportPath, "*.trx", startedUtc, ParseTrx),
                TestReportFormat.JUnitXml => FromFiles(command.ReportPath, "*.xml", startedUtc, ParseJUnit),
                TestReportFormat.JestJson => FromFiles(command.ReportPath, "*.json", startedUtc, ParseJestJson),
                TestReportFormat.MochaJson => FromFiles(command.ReportPath, "*.json", startedUtc, ParseMochaJson),
                TestReportFormat.GoJson => (ParseGoJson(output), null),
                TestReportFormat.RustText => (ParseRust(output), null),
                _ => ([], "Runner ini hanya memberi exit code, tanpa hasil per test.")
            };
        }
        catch (Exception ex) when (ex is XmlException or JsonException or IOException or UnauthorizedAccessException or FormatException)
        {
            return ([], $"Laporan hasil test tidak terbaca: {ex.Message}");
        }
    }

    private static (IReadOnlyList<TestCaseOutcome>, string?) FromFiles(string? path, string pattern, DateTime startedUtc, Func<string, IEnumerable<TestCaseOutcome>> parse)
    {
        if (path is null) return ([], "Runner tidak menulis laporan hasil test.");
        IEnumerable<string> files;
        if (File.Exists(path)) files = [path];
        else if (Directory.Exists(path))
            // A little slack: some file systems round write times.
            files = Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly)
                .Where(f => File.GetLastWriteTimeUtc(f) >= startedUtc.AddSeconds(-2))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        else return ([], "Runner tidak menulis laporan hasil test (biasanya karena gagal build atau gagal start).");

        var outcomes = new List<TestCaseOutcome>();
        foreach (var file in files) outcomes.AddRange(parse(File.ReadAllText(file)));
        return outcomes.Count == 0 && !files.Any()
            ? ([], "Runner tidak menulis laporan hasil test (biasanya karena gagal build atau gagal start).")
            : (outcomes, null);
    }

    // TRX: results reference their test definition by testId; the definition names the class and the method.
    public static IEnumerable<TestCaseOutcome> ParseTrx(string xml)
    {
        var document = XDocument.Parse(xml);
        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        var methods = document.Descendants(ns + "UnitTest")
            .Select(u => (Id: (string?)u.Attribute("id"), Method: u.Element(ns + "TestMethod")))
            .Where(u => u.Id is not null && u.Method is not null)
            .GroupBy(u => u.Id!)
            .ToDictionary(g => g.Key, g => g.First().Method!);

        foreach (var result in document.Descendants(ns + "UnitTestResult"))
        {
            var testId = (string?)result.Attribute("testId");
            var testName = (string?)result.Attribute("testName") ?? string.Empty;
            string? className = null;
            var name = testName;
            if (testId is not null && methods.TryGetValue(testId, out var method))
            {
                className = (string?)method.Attribute("className");
                name = (string?)method.Attribute("name") ?? testName;
            }
            // className is sometimes "Ns.Class, Assembly".
            var comma = className?.IndexOf(',') ?? -1;
            if (comma > 0) className = className![..comma];

            var error = result.Element(ns + "Output")?.Element(ns + "ErrorInfo");
            var message = Join((string?)error?.Element(ns + "Message"), (string?)error?.Element(ns + "StackTrace"));
            var outcome = (string?)result.Attribute("outcome") ?? string.Empty;
            var status = outcome switch
            {
                "Passed" or "PassedButRunAborted" => UnitTestResult.Passed,
                "Failed" => UnitTestResult.Failed,
                "NotExecuted" or "Inconclusive" or "Pending" or "Disconnected" or "Warning" => UnitTestResult.Skipped,
                _ => UnitTestResult.Error
            };
            yield return new TestCaseOutcome(className, StripParameters(name), null, status, Duration((string?)result.Attribute("duration")),
                status == UnitTestResult.Skipped ? null : Clip(message),
                status == UnitTestResult.Skipped ? Clip((string?)error?.Element(ns + "Message")) ?? $"Tidak dijalankan ({outcome})." : null);
        }
    }

    public static IEnumerable<TestCaseOutcome> ParseJUnit(string xml)
    {
        var document = XDocument.Parse(xml);
        foreach (var testCase in document.Descendants().Where(e => e.Name.LocalName == "testcase"))
        {
            var className = (string?)testCase.Attribute("classname");
            var name = (string?)testCase.Attribute("name") ?? string.Empty;
            var file = (string?)testCase.Attribute("file");
            var seconds = double.TryParse((string?)testCase.Attribute("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
            var failure = testCase.Elements().FirstOrDefault(e => e.Name.LocalName == "failure");
            var error = testCase.Elements().FirstOrDefault(e => e.Name.LocalName == "error");
            var skipped = testCase.Elements().FirstOrDefault(e => e.Name.LocalName == "skipped");

            var (status, problem) = failure is not null ? (UnitTestResult.Failed, failure)
                : error is not null ? (UnitTestResult.Error, error)
                : skipped is not null ? (UnitTestResult.Skipped, skipped)
                : (UnitTestResult.Passed, (XElement?)null);
            var message = problem is null ? null : Join((string?)problem.Attribute("message"), problem.Value);
            yield return new TestCaseOutcome(className, StripParameters(name), file, status, (long)(seconds * 1000),
                status == UnitTestResult.Skipped ? null : Clip(message),
                status == UnitTestResult.Skipped ? Clip(message) ?? "Di-skip oleh test itu sendiri tanpa alasan." : null);
        }
    }

    // jest --json and vitest --reporter=json: per file a list of assertion results; a file that failed to load has a
    // message and no results.
    public static IEnumerable<TestCaseOutcome> ParseJestJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var outcomes = new List<TestCaseOutcome>();
        if (!document.RootElement.TryGetProperty("testResults", out var files) || files.ValueKind != JsonValueKind.Array) return outcomes;

        foreach (var file in files.EnumerateArray())
        {
            var path = Text(file, "name") ?? Text(file, "testFilePath");
            var tests = file.TryGetProperty("assertionResults", out var assertions) && assertions.ValueKind == JsonValueKind.Array ? assertions
                : file.TryGetProperty("testResults", out var nested) && nested.ValueKind == JsonValueKind.Array ? nested
                : default;
            var count = 0;
            if (tests.ValueKind == JsonValueKind.Array)
            {
                foreach (var test in tests.EnumerateArray())
                {
                    count++;
                    var status = Text(test, "status") switch
                    {
                        "passed" => UnitTestResult.Passed,
                        "failed" => UnitTestResult.Failed,
                        _ => UnitTestResult.Skipped
                    };
                    var failures = test.TryGetProperty("failureMessages", out var messages) && messages.ValueKind == JsonValueKind.Array
                        ? string.Join("\n", messages.EnumerateArray().Select(m => m.ValueKind == JsonValueKind.String ? m.GetString() : m.ToString()))
                        : null;
                    var duration = test.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? (long)d.GetDouble() : 0;
                    outcomes.Add(new TestCaseOutcome(null, Text(test, "title") ?? string.Empty, path, status, duration,
                        status == UnitTestResult.Failed ? Clip(StripAnsi(failures)) : null,
                        status == UnitTestResult.Skipped ? $"Di-skip di kode test ({Text(test, "status")})." : null));
                }
            }
            if (count == 0 && Text(file, "status") == "failed")
                outcomes.Add(new TestCaseOutcome(null, TestCaseOutcome.Everything, path, UnitTestResult.Error, 0, Clip(StripAnsi(Text(file, "message"))) ?? "File test gagal dimuat."));
        }
        return outcomes;
    }

    public static IEnumerable<TestCaseOutcome> ParseMochaJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var outcomes = new List<TestCaseOutcome>();
        var failed = Titles(root, "failures");
        var pending = Titles(root, "pending");
        if (!root.TryGetProperty("tests", out var tests) || tests.ValueKind != JsonValueKind.Array) return outcomes;

        foreach (var test in tests.EnumerateArray())
        {
            var title = Text(test, "title") ?? string.Empty;
            var key = Text(test, "fullTitle") ?? title;
            var status = failed.Contains(key) ? UnitTestResult.Failed : pending.Contains(key) ? UnitTestResult.Skipped : UnitTestResult.Passed;
            var message = test.TryGetProperty("err", out var err) && err.ValueKind == JsonValueKind.Object ? Join(Text(err, "message"), Text(err, "stack")) : null;
            var duration = test.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? (long)d.GetDouble() : 0;
            outcomes.Add(new TestCaseOutcome(null, title, Text(test, "file"), status, duration,
                status == UnitTestResult.Failed ? Clip(message) : null,
                status == UnitTestResult.Skipped ? "Di-skip di kode test (pending)." : null));
        }
        return outcomes;
    }

    // go test -json: one event per line; the last pass/fail/skip of a top-level test decides. A package that does not
    // build ends with a fail event without a test.
    public static IReadOnlyList<TestCaseOutcome> ParseGoJson(string output)
    {
        var results = new Dictionary<(string Package, string Test), (string Action, double Elapsed)>();
        var logs = new Dictionary<(string, string), StringBuilder>();
        var packageLogs = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        var packageFailed = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('{')) continue;
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                continue;
            }
            using (document)
            {
                var e = document.RootElement;
                var action = Text(e, "Action");
                var package = Text(e, "Package") ?? string.Empty;
                var test = Text(e, "Test");
                if (test is null)
                {
                    if (action == "output") (packageLogs.TryGetValue(package, out var packageLog) ? packageLog : packageLogs[package] = new StringBuilder()).Append(Text(e, "Output"));
                    else if (action is "pass" or "fail" or "skip") packageFailed[package] = action == "fail";
                    continue;
                }
                if (test.Contains('/')) continue;
                var key = (package, test);
                if (action == "output") (logs.TryGetValue(key, out var log) ? log : logs[key] = new StringBuilder()).Append(Text(e, "Output"));
                else if (action is "pass" or "fail" or "skip")
                    results[key] = (action, e.TryGetProperty("Elapsed", out var elapsed) && elapsed.ValueKind == JsonValueKind.Number ? elapsed.GetDouble() : 0);
            }
        }

        var outcomes = results.Select(r =>
        {
            var status = r.Value.Action switch { "pass" => UnitTestResult.Passed, "fail" => UnitTestResult.Failed, _ => UnitTestResult.Skipped };
            var log = logs.GetValueOrDefault(r.Key)?.ToString();
            return new TestCaseOutcome(r.Key.Package, r.Key.Test, null, status, (long)(r.Value.Elapsed * 1000),
                status == UnitTestResult.Failed ? Clip(log) : null,
                status == UnitTestResult.Skipped ? Clip(log) ?? "Di-skip di kode test (t.Skip)." : null);
        }).ToList();
        // A package that failed without any test result did not build.
        foreach (var package in packageFailed.Where(p => p.Value && !results.Keys.Any(k => k.Package == p.Key)).Select(p => p.Key))
            outcomes.Add(new TestCaseOutcome(package, TestCaseOutcome.Everything, null, UnitTestResult.Error, 0,
                Clip(packageLogs.GetValueOrDefault(package)?.ToString()) ?? "Package gagal di-build."));
        return outcomes;
    }

    // cargo test: "test module::name ... ok|FAILED|ignored", then a "---- module::name stdout ----" block per failure.
    public static IReadOnlyList<TestCaseOutcome> ParseRust(string output)
    {
        var lines = output.Replace("\r", string.Empty).Split('\n');
        var details = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        string? current = null;
        foreach (var line in lines)
        {
            var header = RustFailureHeader().Match(line);
            if (header.Success)
            {
                current = header.Groups[1].Value;
                details[current] = new StringBuilder();
                continue;
            }
            if (current is null) continue;
            if (line.StartsWith("failures:", StringComparison.Ordinal) || line.StartsWith("test result:", StringComparison.Ordinal)) current = null;
            else details[current].Append(line).Append('\n');
        }

        var outcomes = new List<TestCaseOutcome>();
        foreach (Match match in RustResultLine().Matches(string.Join('\n', lines)))
        {
            var path = match.Groups["name"].Value;
            var verdict = match.Groups["result"].Value;
            var status = verdict == "ok" ? UnitTestResult.Passed : verdict.StartsWith("FAILED", StringComparison.Ordinal) ? UnitTestResult.Failed : UnitTestResult.Skipped;
            var cut = path.LastIndexOf("::", StringComparison.Ordinal);
            outcomes.Add(new TestCaseOutcome(cut < 0 ? null : path[..cut], cut < 0 ? path : path[(cut + 2)..], null, status, 0,
                status == UnitTestResult.Failed ? Clip(details.GetValueOrDefault(path)?.ToString()) : null,
                status == UnitTestResult.Skipped ? (verdict.Length > "ignored".Length ? $"Di-skip di kode test: {verdict["ignored, ".Length..]}" : "Di-skip di kode test (#[ignore]).") : null));
        }
        return outcomes;
    }

    // "Method(x: 1)" → "Method", "test_add[1-2]" → "test_add", "testFoo with data set #0" → "testFoo", "should work()" → "should work".
    public static string StripParameters(string name)
    {
        var text = name.Trim();
        var dataSet = text.IndexOf(" with data set ", StringComparison.Ordinal);
        if (dataSet > 0) text = text[..dataSet];
        foreach (var open in new[] { '(', '[' })
        {
            var index = text.IndexOf(open);
            if (index > 0) text = text[..index];
        }
        return text.Trim();
    }

    private static HashSet<string> Titles(JsonElement root, string property)
    {
        var titles = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var item in list.EnumerateArray())
                if ((Text(item, "fullTitle") ?? Text(item, "title")) is { } title) titles.Add(title);
        return titles;
    }

    private static long Duration(string? value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) ? (long)duration.TotalMilliseconds : 0;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Join(string? first, string? second)
    {
        var parts = new[] { first?.Trim(), second?.Trim() }.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    public static string? Clip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length <= MaxMessageChars ? trimmed : trimmed[..MaxMessageChars] + "…";
    }

    private static string? StripAnsi(string? text) => text is null ? null : Ansi().Replace(text, string.Empty);

    [GeneratedRegex(@"\x1B\[[0-9;]*m")]
    private static partial Regex Ansi();

    [GeneratedRegex(@"^---- (\S+) stdout ----$")]
    private static partial Regex RustFailureHeader();

    [GeneratedRegex(@"^test (?<name>\S+) \.\.\. (?<result>ok|FAILED|ignored(?:, .+)?)$", RegexOptions.Multiline)]
    private static partial Regex RustResultLine();
}
