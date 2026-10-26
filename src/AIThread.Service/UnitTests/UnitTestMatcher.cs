using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Verification;

namespace AIThread.Service.UnitTests;

// The result of one catalog test in one run. Requirement: what the machine lacked when the test was skipped for its
// environment (TestEnvironment); Flaky: it failed and passed on the retry; Remembered: not run now, an earlier skip on
// this machine stands for it (not recorded as a new run, so the skip expires).
public sealed record TestVerdict(
    UnitTestResult Result,
    long? DurationMs,
    string? Message,
    string? SkipReason,
    string? Requirement = null,
    bool Flaky = false,
    bool Remembered = false)
{
    public bool IsFailure => Result is UnitTestResult.Failed or UnitTestResult.Error;
}

// Finds a catalog test among the outcomes a runner reported, whatever naming the framework uses, and folds data rows
// (theories, parametrized tests) into one verdict.
public static class UnitTestMatcher
{
    public static TestVerdict? Match(UnitTestCase test, TestSuite suite, IReadOnlyList<TestCaseOutcome> outcomes)
    {
        var matched = outcomes.Where(o => Matches(test, suite, o)).ToList();
        return matched.Count == 0 ? null : Aggregate(matched);
    }

    // A failure caused by the machine (no database, no network) becomes a skip with its reason.
    public static TestVerdict Aggregate(IReadOnlyList<TestCaseOutcome> outcomes)
    {
        var duration = outcomes.Sum(o => o.DurationMs);
        var failed = outcomes.FirstOrDefault(o => o.Result == UnitTestResult.Failed) ?? outcomes.FirstOrDefault(o => o.Result == UnitTestResult.Error);
        if (failed is not null)
        {
            if (TestEnvironment.Classify(failed.Message) is { } environment)
                return new TestVerdict(UnitTestResult.Skipped, duration, failed.Message, environment.Reason, environment.Requirement);
            return new TestVerdict(failed.Result, duration, failed.Message, null);
        }
        if (outcomes.Any(o => o.Result == UnitTestResult.Passed)) return new TestVerdict(UnitTestResult.Passed, duration, null, null);
        var skipped = outcomes.First();
        return new TestVerdict(UnitTestResult.Skipped, duration, null, skipped.SkipReason ?? Lang.T("Di-skip oleh runner tanpa alasan.", "Skipped by the runner without a reason."));
    }

    public static bool Matches(UnitTestCase test, TestSuite suite, TestCaseOutcome outcome)
    {
        if (outcome.Name == TestCaseOutcome.Everything)
            return (outcome.File is not null && SameFile(outcome.File, suite.Root, test.TestFile))
                   || (suite.Framework == TestSuite.Go && outcome.ClassName is not null && SamePackage(outcome.ClassName, test.ClassName));

        switch (suite.Framework)
        {
            case TestSuite.XUnit or TestSuite.NUnit or TestSuite.MsTest or TestSuite.DotNet:
                var full = outcome.ClassName is null ? outcome.Name : $"{outcome.ClassName}.{outcome.Name}";
                return string.Equals(full, test.Selector, StringComparison.Ordinal)
                       || string.Equals(full.Replace('+', '.'), test.Selector.Replace('+', '.'), StringComparison.Ordinal);
            case TestSuite.JUnit:
                return outcome.Name == test.MethodName.Trim('`')
                       && (outcome.ClassName == test.ClassName || TestCommandResolver.ShortClass(outcome.ClassName) == TestCommandResolver.ShortClass(test.ClassName));
            case TestSuite.Pytest:
                return outcome.Name == test.MethodName && SamePythonClass(outcome, suite, test);
            case TestSuite.PhpUnit:
                return outcome.Name == test.MethodName
                       && TestCommandResolver.ShortClass(outcome.ClassName) == TestCommandResolver.ShortClass(test.ClassName);
            case TestSuite.Jest or TestSuite.Vitest or TestSuite.Mocha:
                return outcome.Name == test.MethodName && (outcome.File is null || SameFile(outcome.File, suite.Root, test.TestFile));
            case TestSuite.Go:
                return outcome.Name == test.MethodName && (outcome.ClassName is null || SamePackage(outcome.ClassName, test.ClassName));
            case TestSuite.Rust:
                return outcome.Name == test.MethodName;
            default:
                return false;
        }
    }

    // A path as the runner wrote it (absolute, or relative to its folder) against a path relative to the root.
    public static bool SameFile(string reported, string root, string relative)
    {
        var wanted = relative.Replace('\\', '/').Trim('/');
        var path = reported.Replace('\\', '/');
        if (Path.IsPathRooted(reported))
        {
            try
            {
                path = Path.GetRelativePath(root, reported).Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
        path = path.TrimStart('.', '/');
        return string.Equals(path, wanted, StringComparison.OrdinalIgnoreCase)
               || wanted.EndsWith("/" + path, StringComparison.OrdinalIgnoreCase)
               || path.EndsWith("/" + wanted, StringComparison.OrdinalIgnoreCase);
    }

    // go: the import path ends with the folder of the test file (the root package matches any).
    private static bool SamePackage(string importPath, string? folder) =>
        string.IsNullOrEmpty(folder) || importPath == folder || importPath.EndsWith("/" + folder, StringComparison.Ordinal);

    // pytest: classname is the dotted module path (relative to where pytest runs), then the class.
    private static bool SamePythonClass(TestCaseOutcome outcome, TestSuite suite, UnitTestCase test)
    {
        if (outcome.File is not null) return SameFile(outcome.File, suite.Directory, Path.GetRelativePath(suite.Directory, Path.Combine(suite.Root, test.TestFile)));
        if (outcome.ClassName is null) return true;
        var module = Path.ChangeExtension(Path.GetRelativePath(suite.Directory, Path.Combine(suite.Root, test.TestFile)), null)!.Replace('\\', '/').Replace('/', '.');
        var expected = test.ClassName is null ? module : $"{module}.{test.ClassName}";
        return outcome.ClassName == expected
               || outcome.ClassName.EndsWith("." + expected, StringComparison.Ordinal)
               || expected.EndsWith("." + outcome.ClassName, StringComparison.Ordinal);
    }
}
