using System.Text.Json.Nodes;
using AIThread.Service.Memory;
using AIThread.Service.Testing;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The window opens on the first browser tool, so an API-only run never shows one.
    private async Task<string> BrowserAsync(string tool, JsonObject args, TestRun run, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (run.BrowserError is { } failed) return $"Error: {failed}";
        if (run.Browser is null)
        {
            try
            {
                run.Browser = await browsers.LaunchAsync(text => progress.DetailAsync(text, cancellationToken), cancellationToken);
                run.Report.Browser = run.Browser.Name;
                await progress.DetailAsync($"🌐 {run.Browser.Name} dibuka (non-headless)", cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                run.BrowserError = $"the browser could not be started ({ex.Message}). Mark the UI scenarios \"blocked\".";
                run.Report.Error ??= $"Browser tester tidak bisa dibuka: {ex.Message}";
                return $"Error: {run.BrowserError}";
            }
        }

        var browser = run.Browser;
        switch (tool)
        {
            case TesterTools.BrowserOpen:
                var target = PlannerToolRunner.Str(args, "url") ?? "/";
                return TestUrls.Resolve(run.Test.BaseUrl!, target) is { } url
                    ? await browser.OpenAsync(url)
                    : $"Error: '{target}' is not on this machine. The tester only opens the app at {run.Test.BaseUrl} (localhost).";
            case TesterTools.BrowserSnapshot:
                return await browser.SnapshotAsync();
            case TesterTools.BrowserPress:
                return PlannerToolRunner.Str(args, "key") is { } key ? await browser.PressAsync(key) : "Error: \"key\" is required.";
            case TesterTools.BrowserWait:
                var seconds = args["seconds"] is JsonValue v && v.TryGetValue<double>(out var s) ? s : 5;
                return await browser.WaitAsync(PlannerToolRunner.Str(args, "text"), PlannerToolRunner.Bool(args, "gone"), seconds);
        }

        if (Element(args) is not { } element) return "Error: give \"ref\" (from the latest snapshot) or \"selector\".";
        return tool switch
        {
            TesterTools.BrowserClick => await browser.ClickAsync(element),
            TesterTools.BrowserFill => args["text"] is JsonValue text && text.TryGetValue<string>(out var value)
                ? await browser.FillAsync(element, value, PlannerToolRunner.Bool(args, "submit"))
                : "Error: \"text\" is required (an empty string clears the field).",
            TesterTools.BrowserSelect => MemoryAi.Strings(args["values"]) is { Count: > 0 } values
                ? await browser.SelectAsync(element, values)
                : "Error: \"values\" is required.",
            _ => $"Error: unknown tool '{tool}'."
        };
    }

    private static TestElement? Element(JsonObject args)
    {
        var reference = PlannerToolRunner.Str(args, "ref");
        var selector = PlannerToolRunner.Str(args, "selector");
        if (reference is null && selector is null) return null;
        return new TestElement(PlannerToolRunner.Str(args, "element") ?? selector ?? reference!, reference, selector);
    }
}
