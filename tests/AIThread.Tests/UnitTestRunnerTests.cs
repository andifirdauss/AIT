using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Tests;

// The pieces that run unit tests without a model: finding the suites and the tests of a project, the commands, the
// reports of every runner, and matching what a runner reported to the catalog.
public class UnitTestRunnerTests
{
    private const string LegacyLibrary = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="12.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" Condition="Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')" />
          <PropertyGroup><TargetFrameworkVersion>v4.5</TargetFrameworkVersion></PropertyGroup>
          <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
        </Project>
        """;

    private const string SdkTestProject =
        """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="xunit" Version="2.6.6" /><ProjectReference Include="..\Lib\Lib.csproj" /></ItemGroup></Project>""";

    [Fact]
    public void An_sdk_style_test_project_that_references_an_old_format_project_is_built_by_msbuild_and_run_by_vstest()
    {
        using var project = new TempWorkspace();
        project.Write("App.sln", "");
        project.Write("Lib/Lib.csproj", LegacyLibrary);
        project.Write("Lib/packages.config", "<packages />");
        project.Write("Tests/Tests.csproj", SdkTestProject);

        var suite = Assert.Single(TestCommandResolver.Suites(project.Root, ["Lib/Lib.csproj", "Tests/Tests.csproj"]), s => s.Manifest == "Tests/Tests.csproj");

        Assert.True(suite.BuildsWithMsBuild);
        Assert.False(suite.IsLegacyDotNet);
        // Visual Studio may not be installed on this machine; then the suite says so instead of running with dotnet.
        if (!suite.CanRun) Assert.Contains("Visual Studio", suite.Problem + BuildCommandResolver.NoMsBuild);

        var reportFile = Path.Combine(project.Root, "report", "report.json");
        var command = TestCommandResolver.Command(suite, [new TestTarget("Tests.UserTests.Saves", "Tests/UserTests.cs", "Tests.UserTests", "Saves")], reportFile);

        Assert.NotNull(command.Prepare);
        Assert.Contains("-restore", command.Prepare.Arguments);
        Assert.Contains("-p:RestorePackagesConfig=true", command.Prepare.Arguments);
        var outDir = Path.Combine(project.Root, "report", "bin") + Path.DirectorySeparatorChar;
        Assert.Contains($"-p:OutDir={outDir}", command.Prepare.Arguments);
        Assert.Contains($"-p:SolutionDir={project.Root}{Path.DirectorySeparatorChar}", command.Prepare.Arguments);
        Assert.Equal(Path.Combine(outDir, "Tests.dll"), command.Arguments[0]);
        Assert.Contains("/TestCaseFilter:FullyQualifiedName=Tests.UserTests.Saves", string.Join(' ', command.Arguments));
    }

    [Fact]
    public void An_sdk_style_test_project_without_old_format_references_keeps_dotnet_test()
    {
        using var project = new TempWorkspace();
        project.Write("Lib/Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>""");
        project.Write("Tests/Tests.csproj", SdkTestProject);

        var suite = Assert.Single(TestCommandResolver.Suites(project.Root, ["Lib/Lib.csproj", "Tests/Tests.csproj"]), s => s.Manifest == "Tests/Tests.csproj");

        Assert.False(suite.BuildsWithMsBuild);
        Assert.Null(TestCommandResolver.Command(suite, null, Path.Combine(project.Root, "r", "report.json")).Prepare);
    }

    [Fact]
    public void Suites_are_found_per_stack_with_the_reason_a_suite_cannot_run()
    {
        using var project = new TempWorkspace();
        project.Write("App.slnx", """<Solution><Project Path="src/App/App.csproj" /><Project Path="tests/deep/App.Tests/App.Tests.csproj" /></Solution>""");
        project.Write("src/App/App.csproj", """<Project Sdk="Microsoft.NET.Sdk"></Project>""");
        project.Write("tests/deep/App.Tests/App.Tests.csproj",
            """<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="xunit" Version="2.9.3" /></ItemGroup></Project>""");
        project.Write("web/package.json", """{ "devDependencies": { "jest": "^29.0.0" }, "scripts": { "test": "jest" } }""");
        project.Write("web/node_modules/jest/bin/jest.js", "// jest");
        project.Write("ui/package.json", """{ "devDependencies": { "vitest": "^3.0.0" } }""");
        project.Write("tools/package.json", """{ "scripts": { "test": "echo \"Error: no test specified\" && exit 1" } }""");
        project.Write("api/pyproject.toml", "[project]\nname = \"api\"\n");
        project.Write("api/tests/test_calc.py", "def test_add():\n    assert 1 + 1 == 2\n");
        project.Write("php/composer.json", "{}");
        project.Write("php/phpunit.xml", "<phpunit />");
        project.Write("svc/go.mod", "module example.com/svc\n");
        project.Write("svc/calc_test.go", "package svc\n");
        string[] modules = ["App.slnx", "src/App/App.csproj", "web/package.json", "ui/package.json", "tools/package.json", "api/pyproject.toml", "php/composer.json", "svc/go.mod"];

        var suites = TestCommandResolver.Suites(project.Root, modules);

        var dotnet = Assert.Single(suites, s => s.Framework == TestSuite.XUnit);
        Assert.Equal("tests/deep/App.Tests/App.Tests.csproj", dotnet.Manifest);
        Assert.Equal(TestReportFormat.Trx, dotnet.Format);
        Assert.True(Assert.Single(suites, s => s.Framework == TestSuite.Jest).CanRun);
        Assert.Contains("npm install", Assert.Single(suites, s => s.Framework == TestSuite.Vitest).Problem);
        Assert.DoesNotContain(suites, s => s.Manifest == "tools/package.json");
        Assert.Equal("api", Assert.Single(suites, s => s.Framework == TestSuite.Pytest).Manifest);
        Assert.Contains("composer install", Assert.Single(suites, s => s.Framework == TestSuite.PhpUnit).Problem);
        Assert.Equal(TestReportFormat.GoJson, Assert.Single(suites, s => s.Framework == TestSuite.Go).Format);
        Assert.DoesNotContain(suites, s => s.Manifest == "src/App/App.csproj");

        Assert.Equal(dotnet, TestCommandResolver.SuiteFor(suites, project.Root, "tests/deep/App.Tests/UserTests.cs"));
        Assert.Null(TestCommandResolver.SuiteFor(suites, project.Root, "src/App/User.cs"));
        Assert.Equal(TestSuite.Pytest, TestCommandResolver.SuiteFor(suites, project.Root, "api/tests/test_calc.py")!.Framework);
    }

    [Fact]
    public void Commands_run_only_the_chosen_tests_with_names_escaped()
    {
        var dotnet = new TestSuite(TestSuite.XUnit, ".NET", "tests/App.Tests/App.Tests.csproj", "C:/repo/tests/App.Tests", "C:/repo", TestReportFormat.Trx);
        var command = TestCommandResolver.Command(dotnet, [new TestTarget("Ns.UserTests.Save_(x)", "tests/App.Tests/UserTests.cs", "Ns.UserTests", "Save_(x)")], "C:/tmp/r/report.json");
        Assert.Equal("dotnet", command.FileName);
        Assert.Contains("--logger", command.Arguments);
        var filter = command.Arguments[command.Arguments.ToList().IndexOf("--filter") + 1];
        Assert.Equal(@"FullyQualifiedName=Ns.UserTests.Save_\(x\)|FullyQualifiedName~Ns.UserTests.Save_\(x\)\(", filter);
        Assert.True(command.Filtered);

        var whole = TestCommandResolver.Command(dotnet, null, "C:/tmp/r/report.json");
        Assert.DoesNotContain("--filter", whole.Arguments);
        Assert.False(whole.Filtered);

        using var project = new TempWorkspace();
        project.Write("node_modules/jest/bin/jest.js", "// jest");
        var jest = new TestSuite(TestSuite.Jest, "Node.js", "package.json", project.Root, project.Root, TestReportFormat.JestJson);
        var jestCommand = TestCommandResolver.Command(jest, [new TestTarget("src/a.test.ts::adds (1+1)", "src/a.test.ts", null, "adds (1+1)")], Path.Combine(project.Root, "r.json"));
        Assert.Equal("node", jestCommand.FileName);
        Assert.Contains("--runTestsByPath", jestCommand.Arguments);
        Assert.Contains(@"(adds \(1\+1\))$", jestCommand.Arguments);
    }

    [Fact]
    public void Trx_reports_give_class_method_result_and_skip_reason()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="a" testName="Ns.UserTests.Saves" outcome="Passed" duration="00:00:00.0150000" />
                <UnitTestResult testId="b" testName="Ns.UserTests.Rejects(name: &quot;&quot;)" outcome="Failed" duration="00:00:00.0020000">
                  <Output><ErrorInfo><Message>Assert.Equal() Failure</Message><StackTrace>at Ns.UserTests.Rejects()</StackTrace></ErrorInfo></Output>
                </UnitTestResult>
                <UnitTestResult testId="c" testName="Ns.UserTests.Later" outcome="NotExecuted" duration="00:00:00">
                  <Output><ErrorInfo><Message>Not ready yet</Message></ErrorInfo></Output>
                </UnitTestResult>
              </Results>
              <TestDefinitions>
                <UnitTest id="a" name="Saves"><TestMethod className="Ns.UserTests" name="Saves" /></UnitTest>
                <UnitTest id="b" name="Rejects"><TestMethod className="Ns.UserTests, App.Tests" name="Rejects" /></UnitTest>
                <UnitTest id="c" name="Later"><TestMethod className="Ns.UserTests" name="Later" /></UnitTest>
              </TestDefinitions>
            </TestRun>
            """;

        var outcomes = TestResultParser.ParseTrx(trx).ToList();

        Assert.Equal(("Ns.UserTests", "Saves", UnitTestResult.Passed, 15L), (outcomes[0].ClassName, outcomes[0].Name, outcomes[0].Result, outcomes[0].DurationMs));
        Assert.Equal(("Ns.UserTests", "Rejects", UnitTestResult.Failed), (outcomes[1].ClassName, outcomes[1].Name, outcomes[1].Result));
        Assert.Contains("Assert.Equal() Failure", outcomes[1].Message);
        Assert.Equal((UnitTestResult.Skipped, "Not ready yet"), (outcomes[2].Result, outcomes[2].SkipReason));
    }

    [Fact]
    public void JUnit_jest_mocha_go_and_cargo_reports_are_read()
    {
        var junit = TestResultParser.ParseJUnit("""
            <testsuites><testsuite name="pytest">
              <testcase classname="tests.test_calc.TestCalc" name="test_add[1-2]" time="0.01" />
              <testcase classname="tests.test_calc" name="test_div" time="0.02"><failure message="ZeroDivisionError">trace</failure></testcase>
              <testcase classname="tests.test_calc" name="test_slow"><skipped message="needs db" /></testcase>
            </testsuite></testsuites>
            """).ToList();
        Assert.Equal(("test_add", UnitTestResult.Passed, 10L), (junit[0].Name, junit[0].Result, junit[0].DurationMs));
        Assert.Equal(UnitTestResult.Failed, junit[1].Result);
        Assert.Equal("needs db", junit[2].SkipReason);

        var jest = TestResultParser.ParseJestJson("""
            { "testResults": [
              { "name": "/repo/src/a.test.ts", "status": "failed", "assertionResults": [
                { "title": "adds", "status": "passed", "duration": 3, "failureMessages": [] },
                { "title": "divides", "status": "failed", "failureMessages": ["\u001b[31mexpected 2\u001b[39m"] },
                { "title": "later", "status": "pending", "failureMessages": [] } ] },
              { "name": "/repo/src/broken.test.ts", "status": "failed", "message": "SyntaxError: Unexpected token", "assertionResults": [] } ] }
            """).ToList();
        Assert.Equal([UnitTestResult.Passed, UnitTestResult.Failed, UnitTestResult.Skipped, UnitTestResult.Error], jest.Select(o => o.Result));
        Assert.Equal("expected 2", jest[1].Message);
        Assert.Equal((TestCaseOutcome.Everything, "/repo/src/broken.test.ts"), (jest[3].Name, jest[3].File));

        var mocha = TestResultParser.ParseMochaJson("""
            { "tests": [ { "title": "adds", "fullTitle": "calc adds", "file": "/repo/test/calc.js", "duration": 1, "err": {} },
                         { "title": "divides", "fullTitle": "calc divides", "file": "/repo/test/calc.js", "err": { "message": "boom" } } ],
              "failures": [ { "title": "divides", "fullTitle": "calc divides" } ], "pending": [] }
            """).ToList();
        Assert.Equal([UnitTestResult.Passed, UnitTestResult.Failed], mocha.Select(o => o.Result));

        var go = TestResultParser.ParseGoJson("""
            {"Action":"run","Package":"example.com/svc","Test":"TestAdd"}
            {"Action":"output","Package":"example.com/svc","Test":"TestAdd","Output":"calc_test.go:9: got 3\n"}
            {"Action":"fail","Package":"example.com/svc","Test":"TestAdd","Elapsed":0.01}
            {"Action":"pass","Package":"example.com/svc","Test":"TestSub","Elapsed":0}
            {"Action":"output","Package":"example.com/broken","Output":"broken.go:3: undefined: x\n"}
            {"Action":"fail","Package":"example.com/broken","Elapsed":0}
            """);
        Assert.Equal(UnitTestResult.Failed, go.Single(o => o.Name == "TestAdd").Result);
        Assert.Contains("got 3", go.Single(o => o.Name == "TestAdd").Message);
        Assert.Equal(UnitTestResult.Passed, go.Single(o => o.Name == "TestSub").Result);
        Assert.Equal(("example.com/broken", UnitTestResult.Error), (go.Single(o => o.Name == TestCaseOutcome.Everything).ClassName, go.Single(o => o.Name == TestCaseOutcome.Everything).Result));

        var cargo = TestResultParser.ParseRust("""
            running 3 tests
            test calc::tests::adds ... ok
            test calc::tests::divides ... FAILED
            test calc::tests::slow ... ignored, needs network

            failures:

            ---- calc::tests::divides stdout ----
            thread 'calc::tests::divides' panicked at 'attempt to divide by zero'

            failures:
                calc::tests::divides
            """);
        Assert.Equal([("adds", UnitTestResult.Passed), ("divides", UnitTestResult.Failed), ("slow", UnitTestResult.Skipped)], cargo.Select(o => (o.Name, o.Result)));
        Assert.Contains("divide by zero", cargo[1].Message);
        Assert.Contains("needs network", cargo[2].SkipReason);
    }

    [Fact]
    public void Tests_are_discovered_with_their_lines_in_every_language()
    {
        using var project = new TempWorkspace();
        var dotnet = new TestSuite(TestSuite.XUnit, ".NET", "tests/T.csproj", Path.Combine(project.Root, "tests"), project.Root, TestReportFormat.Trx);
        var csharp = TestDiscovery.InContent("tests/UserTests.cs", """
            namespace Shop.Tests;

            public class UserTests
            {
                // Name is required.
                [Fact]
                public void Save_WithoutName_IsRejected()
                {
                    Assert.True(true);
                }

                public void Helper() { }

                public class Nested
                {
                    [Theory, InlineData(1)]
                    public void Counts(int n) { }
                }
            }
            """, dotnet);
        Assert.Equal(["Shop.Tests.UserTests.Save_WithoutName_IsRejected", "Shop.Tests.UserTests+Nested.Counts"], csharp.Select(t => t.Selector));
        Assert.Equal((5, 10), (csharp[0].StartLine, csharp[0].EndLine));

        var python = TestDiscovery.InContent("api/tests/test_calc.py", """
            import pytest

            def test_add():
                assert 1 + 1 == 2

            class TestDiv:
                @pytest.mark.slow
                def test_by_zero(self):
                    with pytest.raises(ZeroDivisionError):
                        1 / 0

                def helper(self):
                    pass
            """, new TestSuite(TestSuite.Pytest, "Python", "api", Path.Combine(project.Root, "api"), project.Root, TestReportFormat.JUnitXml));
        Assert.Equal(["tests/test_calc.py::test_add", "tests/test_calc.py::TestDiv::test_by_zero"], python.Select(t => t.Selector));
        Assert.Equal((7, 10), (python[1].StartLine, python[1].EndLine));

        var node = new TestSuite(TestSuite.Jest, "Node.js", "package.json", project.Root, project.Root, TestReportFormat.JestJson);
        var js = TestDiscovery.InContent("src/calc.test.ts", """
            describe('calc', () => {
              it('adds two numbers', () => {
                expect(add(1, 1)).toBe(2);
              });
              test("rejects \"NaN\"", () => {});
            });
            """, node);
        Assert.Equal(["adds two numbers", "rejects \"NaN\""], js.Select(t => t.MethodName));
        Assert.Equal((2, 4), (js[0].StartLine, js[0].EndLine));

        var java = TestDiscovery.InContent("src/test/java/shop/UserTest.java", """
            package shop;

            class UserTest {
                @Test
                @DisplayName("save")
                void savesUser() {
                    assertTrue(true);
                }
            }
            """, new TestSuite(TestSuite.JUnit, "Java (Maven)", "pom.xml", project.Root, project.Root, TestReportFormat.JUnitXml));
        Assert.Equal("shop.UserTest#savesUser", Assert.Single(java).Selector);

        var php = TestDiscovery.InContent("tests/UserTest.php", """
            <?php
            namespace Shop\Tests;

            final class UserTest extends TestCase
            {
                public function testSaves(): void { $this->assertTrue(true); }

                /** @test */
                public function rejects_empty_name(): void { }

                private function helper() { }
            }
            """, new TestSuite(TestSuite.PhpUnit, "PHP", "composer.json", project.Root, project.Root, TestReportFormat.JUnitXml));
        Assert.Equal([@"Shop\Tests\UserTest::testSaves", @"Shop\Tests\UserTest::rejects_empty_name"], php.Select(t => t.Selector));

        var go = TestDiscovery.InContent("svc/calc_test.go", "package svc\n\nfunc TestAdd(t *testing.T) {\n\tif add(1, 1) != 2 {\n\t\tt.Fail()\n\t}\n}\n",
            new TestSuite(TestSuite.Go, "Go", "go.mod", project.Root, project.Root, TestReportFormat.GoJson));
        Assert.Equal(("TestAdd", 3, 7), (Assert.Single(go).MethodName, go[0].StartLine, go[0].EndLine));

        var rust = TestDiscovery.InContent("src/calc.rs", "#[cfg(test)]\nmod tests {\n    #[test]\n    fn adds() {\n        assert_eq!(2, 1 + 1);\n    }\n}\n",
            new TestSuite(TestSuite.Rust, "Rust", "Cargo.toml", project.Root, project.Root, TestReportFormat.RustText));
        Assert.Equal(("adds", 3, 6), (Assert.Single(rust).MethodName, rust[0].StartLine, rust[0].EndLine));
    }

    [Fact]
    public void Runner_results_match_catalog_tests_and_environment_failures_become_skips()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));
        var dotnet = new TestSuite(TestSuite.XUnit, ".NET", "tests/T.csproj", Path.Combine(root, "tests"), root, TestReportFormat.Trx);
        var test = new UnitTestCase { Selector = "Ns.UserTests.Rejects", ClassName = "Ns.UserTests", MethodName = "Rejects", TestFile = "tests/UserTests.cs" };
        var verdict = UnitTestMatcher.Match(test, dotnet,
        [
            new TestCaseOutcome("Ns.UserTests", "Rejects", null, UnitTestResult.Passed, 2, null),
            new TestCaseOutcome("Ns.UserTests", "Rejects", null, UnitTestResult.Failed, 3, "Expected 1"),
            new TestCaseOutcome("Ns.UserTests", "RejectsMore", null, UnitTestResult.Passed, 1, null)
        ]);
        Assert.Equal((UnitTestResult.Failed, 5L, "Expected 1"), (verdict!.Result, verdict.DurationMs!.Value, verdict.Message));

        var database = UnitTestMatcher.Match(test, dotnet,
            [new TestCaseOutcome("Ns.UserTests", "Rejects", null, UnitTestResult.Error, 1, "Microsoft.Data.SqlClient.SqlException: A network-related or instance-specific error occurred")]);
        Assert.Equal((UnitTestResult.Skipped, "database"), (database!.Result, database.Requirement));
        Assert.StartsWith("Butuh database", database.SkipReason);

        var pytest = new TestSuite(TestSuite.Pytest, "Python", "api", Path.Combine(root, "api"), root, TestReportFormat.JUnitXml);
        var pythonTest = new UnitTestCase { Selector = "tests/test_calc.py::TestDiv::test_by_zero", ClassName = "TestDiv", MethodName = "test_by_zero", TestFile = "api/tests/test_calc.py" };
        Assert.NotNull(UnitTestMatcher.Match(pythonTest, pytest, [new TestCaseOutcome("tests.test_calc.TestDiv", "test_by_zero", null, UnitTestResult.Passed, 1, null)]));
        Assert.Null(UnitTestMatcher.Match(pythonTest, pytest, [new TestCaseOutcome("tests.test_other.TestDiv", "test_by_zero", null, UnitTestResult.Passed, 1, null)]));

        var jest = new TestSuite(TestSuite.Jest, "Node.js", "package.json", root, root, TestReportFormat.JestJson);
        var jsTest = new UnitTestCase { Selector = "src/a.test.ts::adds", MethodName = "adds", TestFile = "src/a.test.ts" };
        Assert.Equal(UnitTestResult.Error, UnitTestMatcher.Match(jsTest, jest,
            [new TestCaseOutcome(null, TestCaseOutcome.Everything, Path.Combine(root, "src", "a.test.ts"), UnitTestResult.Error, 0, "SyntaxError")])!.Result);
    }

    [Fact]
    public void Related_tests_are_chosen_by_test_file_target_and_module()
    {
        var byFile = new UnitTestCase { TestFile = "tests/UserTests.cs", Module = "User" };
        var byTarget = new UnitTestCase { TestFile = "tests/OrderTests.cs", TargetFiles = "src/Order.cs\nsrc/OrderLine.cs", Module = "Order" };
        var byModule = new UnitTestCase { TestFile = "tests/InvoiceTests.cs", Module = "Invoice" };
        var retired = new UnitTestCase { TestFile = "tests/UserTests.cs", State = UnitTestState.Obsolete };
        UnitTestCase[] tests = [byFile, byTarget, byModule, retired];

        Assert.Equal([byFile, byTarget, byModule], UnitTestSelection.Relevant(tests, UnitTestMode.Relevant, ["tests/UserTests.cs", "./src/OrderLine.cs"], ["invoice"]));
        Assert.Empty(UnitTestSelection.Relevant(tests, UnitTestMode.Relevant, ["src/Other.cs"], []));
        Assert.Equal(3, UnitTestSelection.Relevant(tests, UnitTestMode.All, [], []).Count);
        Assert.Empty(UnitTestSelection.Relevant(tests, UnitTestMode.Off, ["tests/UserTests.cs"], []));
    }

    [Theory]
    [InlineData("Save_WithEmptyName_ReturnsError", "Save with empty name returns error")]
    [InlineData("test_add_two_numbers", "Add two numbers")]
    [InlineData("testSavesHTTPRequest", "Saves HTTP request")]
    [InlineData("adds two numbers", "adds two numbers")]
    public void Method_names_become_readable_titles(string name, string title) => Assert.Equal(title, UnitTestText.Humanize(name));

    [Theory]
    [InlineData("UT-0012", "UT-0012")]
    [InlineData("ut-12", "UT-0012")]
    [InlineData("12", "UT-0012")]
    [InlineData("UT12345", "UT-12345")]
    [InlineData("T-12", null)]
    [InlineData("UT-0", null)]
    public void Test_numbers_are_normalized(string text, string? number) => Assert.Equal(number, UnitTestText.ParseNumber(text));

    [Fact]
    public void Input_hash_changes_with_the_test_or_its_targets_only()
    {
        using var project = new TempWorkspace();
        project.Write("tests/UserTests.cs", "class UserTests {}");
        project.Write("src/User.cs", "class User {}");
        project.Write("src/Other.cs", "class Other {}");
        var test = new UnitTestCase { TestFile = "tests/UserTests.cs", TargetFiles = "src/User.cs" };
        var before = UnitTestText.InputHash(project.Root, test);

        project.Write("src/Other.cs", "class Other { int x; }");
        Assert.Equal(before, UnitTestText.InputHash(project.Root, test));
        project.Write("src/User.cs", "class User { string Name; }");
        Assert.NotEqual(before, UnitTestText.InputHash(project.Root, test));
    }
}
