using AiThread.Common.Enums;
using AiThread.Service.CodeCalls;
using AiThread.Service.Interfaces;
using AiThread.Service.Workspace.Structure;

namespace AiThread.Tests;

// Project > Detail → Struktur kode: declarations read from files without compiling (Roslyn for C#, line patterns for
// the rest), third-party code left unparsed, and partial classes merged in the class view.
public class CodeStructureTests
{
    [Fact]
    public void CSharp_outline_has_namespaces_nested_and_partial_types_with_members()
    {
        var outline = CSharpOutline.Parse("""
            namespace Shop.Logic
            {
                public partial class OrderBL
                {
                    public OrderBL() { }
                    public static Order Get(int id) => null;
                    public string Name { get; set; }
                    public class Line { public void Add() { } }
                }
            }
            """);

        var order = Assert.Single(outline.Types, t => t.Name == "OrderBL");
        Assert.Equal("Shop.Logic", order.Namespace);
        Assert.True(order.IsPartial);
        Assert.Equal(["OrderBL", "Get", "Name"], order.Members.Select(m => m.Name));
        Assert.Equal("constructor", order.Members[0].Kind);
        Assert.Equal("Order Get(int id)", order.Members[1].Signature);
        Assert.Equal(6, order.Members[1].Line);

        var line = Assert.Single(outline.Types, t => t.Name == "Line");
        Assert.Equal("OrderBL", line.Container);
        Assert.Equal("Add", Assert.Single(line.Members).Name);
    }

    [Fact]
    public void Php_outline_separates_methods_from_functions_and_skips_helpers_inside_bodies()
    {
        var outline = PatternOutline.Parse(OutlineLanguages.Php, """
            <?php
            namespace mod_assign\local;

            /**
             * class fake_in_comment
             */
            abstract class grader extends base
            {
                public function __construct($x) {
                    $cb = function() { return 1; };
                }

                public static function &get_instance(): self {
                    if ($a) {
                        function nested_helper() {}
                    }
                }
            }

            function assign_global_helper($id) {
                return $id;
            }
            """);

        var grader = Assert.Single(outline.Types);
        Assert.Equal(("grader", "class", "mod_assign\\local"), (grader.Name, grader.Kind, grader.Namespace));
        Assert.Equal(["__construct", "get_instance"], grader.Members.Select(m => m.Name));
        Assert.Equal("constructor", grader.Members[0].Kind);
        Assert.Equal("assign_global_helper", Assert.Single(outline.Functions).Name);
    }

    [Fact]
    public void Js_outline_reads_classes_prototypes_and_module_functions_but_not_calls()
    {
        var outline = PatternOutline.Parse(OutlineLanguages.JavaScript, """
            define(['jquery'], function($) {
                var Grid = function(root) {
                    this.root = root;
                };
                Grid.prototype.render = function() {
                    $.ajax({ url: '/Grid/Load' });
                };
                const save = async (data) => {
                    doSomething(data, function() {
                        return 1;
                    });
                };
                return {
                    init: function(selector) {
                        new Grid(selector).render();
                    },
                    destroy() {
                    }
                };
            });

            export class Dialog extends Base {
                constructor(title) {
                    super(title);
                    if (title) {
                        show(title);
                    }
                }
                async open(options = {}) {
                }
                close = () => {
                };
            }
            """);

        var grid = Assert.Single(outline.Types, t => t.Name == "Grid");
        Assert.Equal("render", Assert.Single(grid.Members).Name);
        var dialog = Assert.Single(outline.Types, t => t.Name == "Dialog");
        Assert.Equal(["constructor", "open", "close"], dialog.Members.Select(m => m.Name));
        Assert.Equal("constructor", dialog.Members[0].Kind);
        Assert.Equal(["Grid", "save", "init", "destroy"], outline.Functions.Select(f => f.Name));
    }

    [Fact]
    public void Python_and_java_outlines_follow_their_blocks()
    {
        var python = PatternOutline.Parse(OutlineLanguages.Python, """
            class Repo(Base):
                def __init__(self):
                    def inner():
                        pass

                @property
                def name(self):
                    return 1

            def main():
                pass
            """);
        var repo = Assert.Single(python.Types);
        Assert.Equal(["__init__", "name"], repo.Members.Select(m => m.Name));
        Assert.Equal("main", Assert.Single(python.Functions).Name);

        var java = PatternOutline.Parse(OutlineLanguages.Java, """
            package com.shop.order;

            public class OrderService {
                private final Repo repo = new Repo();

                public OrderService(Repo repo) {
                    this.repo = repo;
                }

                public List<Order> findAll(int page) {
                    return repo.list(page);
                }
            }
            """);
        var service = Assert.Single(java.Types);
        Assert.Equal("com.shop.order", service.Namespace);
        Assert.Equal(["OrderService", "findAll"], service.Members.Select(m => m.Name));
        Assert.Equal("constructor", service.Members[0].Kind);
    }

    [Fact]
    public void Minified_javascript_is_not_outlined()
    {
        var minified = "function a(){}" + new string(';', 2_000);
        Assert.Same(FileOutline.Empty, PatternOutline.Parse(OutlineLanguages.JavaScript, minified));
    }

    [Fact]
    public void Scan_skips_declared_third_party_code_and_merges_partial_classes()
    {
        var root = Directory.CreateTempSubdirectory("ait-structure-").FullName;
        try
        {
            Write(root, "Logic/OrderBL.cs", "namespace Shop { public partial class OrderBL { public void Save() { } } }");
            Write(root, "Logic/OrderBL.Load.cs", "namespace Shop { public partial class OrderBL { public void Load() { } } }");
            Write(root, "lib/thirdpartylibs.xml", "<libraries><library><location>adodb</location></library></libraries>");
            Write(root, "lib/adodb/adodb.php", "<?php\nclass ADOConnection {\n    function Connect() {}\n}\n");
            Write(root, "lib/weblib.php", "<?php\nfunction format_text($t) {\n    return $t;\n}\n");
            Write(root, "amd/build/grid.min.js", "define([],function(){})");
            Write(root, "bin/Debug/skip.cs", "class Skipped {}");

            var snapshot = CodeStructureScanner.Scan(root, [], null, CancellationToken.None);

            Assert.DoesNotContain(snapshot.Files.Keys, k => k.StartsWith("bin/"));
            Assert.True(snapshot.Files["lib/adodb/adodb.php"].ThirdParty);
            Assert.Null(snapshot.Files["lib/adodb/adodb.php"].Outline);
            Assert.True(snapshot.Files["amd/build/grid.min.js"].ThirdParty);
            Assert.Equal("format_text", Assert.Single(snapshot.Files["lib/weblib.php"].Outline!.Functions).Name);

            var order = Assert.Single(snapshot.Classes.TypesIn("Shop"));
            Assert.True(order.IsPartial);
            Assert.Equal(2, order.Locations.Count);
            Assert.Equal(["Load", "Save"], order.Members.Select(m => m.Member.Name).Order());

            // A second scan reuses the outlines of unchanged files.
            var again = CodeStructureScanner.Scan(root, [], snapshot, CancellationToken.None);
            Assert.Equal(0, again.Reparsed);
            Assert.Same(snapshot.Files["Logic/OrderBL.cs"].Outline, again.Files["Logic/OrderBL.cs"].Outline);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Refresh_after_a_thread_rescans_in_the_background()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        harness.Project.Write("Logic/OrderBL.cs", "namespace Shop { public class OrderBL { public void Save() { } } }");
        var service = harness.Service<ICodeStructureService>();
        Assert.Equal(1, (await service.GetSummaryAsync(harness.ProjectId, rescan: false)).Types);

        // Threads in two CLIs finish one after the other; the snapshot is still fresh, so only the refresh picks the
        // files up. The second request replaces the first and scans both files.
        harness.Project.Write("Logic/InvoiceBL.cs", "namespace Shop { public class InvoiceBL { public void Post() { } } }");
        service.Refresh(harness.ProjectId);
        harness.Project.Write("Logic/StockBL.cs", "namespace Shop { public class StockBL { public void Move() { } } }");
        service.Refresh(harness.ProjectId);

        var types = 0;
        for (var i = 0; i < 50 && types != 3; i++)
        {
            await Task.Delay(100);
            types = (await service.GetSummaryAsync(harness.ProjectId, rescan: false)).Types;
        }
        Assert.Equal(3, types);
    }

    [Fact]
    public void Outline_end_lines_close_with_the_body_and_callbacks_are_not_functions()
    {
        var js = PatternOutline.Parse(OutlineLanguages.JavaScript, """
            function p_InitForm() {
                p_InitiateDetail();
                p_initiateData();
            }

            function p_initiateData() {
                var label = "{ not a brace }"; // nor this }
                $.ajax({
                    url: "/Transaction/KlaimPending/InitiateData",
                    success: function (retDat) {
                        p_DataToUI(retDat);
                    },
                    error: function (retDat) {
                    }
                });
            }
            """);
        Assert.Equal([("p_InitForm", 1, 4), ("p_initiateData", 6, 16)], js.Functions.Select(f => (f.Name, f.Line, f.EndLine)));

        var php = PatternOutline.Parse(OutlineLanguages.Php, """
            <?php
            abstract class base_grader
            {
                abstract protected function grade($x);

                public function save($x)
                {
                    return $this->grade($x);
                }
            }
            """);
        var grader = Assert.Single(php.Types);
        Assert.Equal((2, 10), (grader.Line, grader.EndLine));
        Assert.Equal([("grade", 4, 4), ("save", 6, 9)], grader.Members.Select(m => (m.Name, m.Line, m.EndLine)));

        var cs = CSharpOutline.Parse("class A\n{\n    void Run()\n    {\n        Go();\n    }\n}\n");
        Assert.Equal((3, 6), (cs.Types[0].Members[0].Line, cs.Types[0].Members[0].EndLine));

        Assert.Equal((3, 4), FunctionSpans.ChangedRange(["a", "b", "c", "d"], ["a", "b", "x", "y", "d"]));
        Assert.Null(FunctionSpans.ChangedRange(["a"], ["a"]));
    }

    [Fact]
    public void Calls_resolve_to_controller_actions_types_and_the_calling_file()
    {
        var root = Directory.CreateTempSubdirectory("ait-calls-").FullName;
        try
        {
            Write(root, "Controllers/Transaksi/KlaimPendingController.cs", "public class KlaimPendingController { public object InitiateData() { return null; } public object Index() { return null; } }");
            Write(root, "BussLogic/clsKlaimDFBL.GetByDocNo.cs", "public partial class clsKlaimDFBL { public static void GetByDocNo(string no) { } }");
            Write(root, "Scripts/global.js", "var clsGlobal = {\n    showLoading: function () {\n    }\n};\n");
            Write(root, "Scripts/a.js", "function init() {\n}\n");
            Write(root, "Scripts/b.js", "function init() {\n}\n");
            var index = CodeStructureScanner.Scan(root, [], null, CancellationToken.None).Symbols;
            var caller = PatternOutline.Parse(OutlineLanguages.JavaScript, "function p_InitForm() {\n    p_load();\n}\nfunction p_load() {\n}\n");

            Assert.Equal("KlaimPendingController.InitiateData", CallResolver.Resolve(CodeCallKind.Http, "POST /Transaction/KlaimPending/InitiateData", "Scripts/x.js", caller, index)?.Symbol);
            Assert.Equal("KlaimPendingController.Index", CallResolver.Resolve(CodeCallKind.Http, "/KlaimPending", "Scripts/x.js", caller, index)?.Symbol);
            Assert.Equal("clsKlaimDFBL.GetByDocNo", CallResolver.Resolve(CodeCallKind.Method, "clsKlaimDFBL.GetByDocNo(txt)", "Controllers/x.cs", caller, index)?.Symbol);
            Assert.Equal("Scripts/global.js", CallResolver.Resolve(CodeCallKind.Method, "clsGlobal.showLoading", "Scripts/x.js", caller, index)?.Path);
            Assert.Equal(("Scripts/x.js", 4), CallResolver.Resolve(CodeCallKind.Function, "p_load", "Scripts/x.js", caller, index) is { } local ? (local.Path, local.Line) : default);
            // Two files declare init(): not a guess.
            Assert.Null(CallResolver.Resolve(CodeCallKind.Function, "init", "Other/x.js", caller, index));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Tools_node_reads_the_calls_of_the_functions_in_the_lines_read_and_unwritten_calls_are_dropped()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        harness.Project.Write("Controllers/KlaimPendingController.cs", "public class KlaimPendingController { public object InitiateData() { return null; } }");
        harness.Project.Write("Scripts/klaim.js", """
            function p_InitForm() {
                p_InitiateDetail();
                p_initiateData();
            }

            function p_initiateData() {
                $.ajax({ type: "POST", url: "/Transaction/KlaimPending/InitiateData" });
            }

            function p_InitiateDetail() {
            }
            """);
        harness.Router.On(ScriptedAgent.FunctionCalls, ScriptedRouter.Text("""
            {"functions":[
              {"id":"F1","calls":[
                {"kind":"function","target":"p_InitiateDetail","line":2},
                {"kind":"function","target":"p_initiateData","line":3},
                {"kind":"function","target":"p_ShowLOV","line":3}]},
              {"id":"F2","calls":[{"kind":"http","target":"POST /Transaction/KlaimPending/InitiateData","line":7}]}]}
            """));

        var scanner = harness.Service<IFunctionCallScanner>();
        await scanner.ScanFileAsync(harness.ProjectId, harness.Project.Root, "Scripts/klaim.js", [(1, 8)], CancellationToken.None);

        var structure = harness.Service<ICodeStructureService>();
        var file = await structure.GetFileAsync(harness.ProjectId, "Scripts/klaim.js");
        var init = file.Functions.Single(f => f.Name == "p_InitForm");
        Assert.Equal(2, init.Calls);
        Assert.Null(file.Functions.Single(f => f.Name == "p_InitiateDetail").Calls); // outside the lines read

        var calls = (await structure.GetCallsAsync(harness.ProjectId, init.Key!))!;
        Assert.Equal(["p_InitiateDetail", "p_initiateData"], calls.Calls.Select(c => c.Target)); // p_ShowLOV is not written there
        Assert.All(calls.Calls, c => Assert.Equal("Scripts/klaim.js", c.ResolvedPath));
        Assert.False(calls.Changed);

        var ajax = (await structure.GetCallsAsync(harness.ProjectId, file.Functions.Single(f => f.Name == "p_initiateData").Key!))!;
        Assert.Equal("KlaimPendingController.InitiateData", Assert.Single(ajax.Calls).ResolvedSymbol);

        // Unchanged functions are not sent again.
        await scanner.ScanFileAsync(harness.ProjectId, harness.Project.Root, "Scripts/klaim.js", [(1, 8)], CancellationToken.None);
        Assert.Single(harness.Router.CallsTo(ScriptedAgent.FunctionCalls));
    }

    [Fact]
    public async Task A_finished_thread_has_only_the_functions_it_changed_read()
    {
        const string file = "Scripts/order.js";
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddStandardNodesAsync();
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        harness.Project.Write(file, "function p_load() {\n    p_fetch();\n}\n\nfunction p_save() {\n    p_post();\n}\n");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new Service.Orchestration.ThreadPlan
            {
                Summary = "Notifikasi setelah simpan.",
                Tasks = [ThreadOrchestratorTests.Step("T1", file, "Call p_notify() after p_post() in p_save.")]
            }))
            .OnExecutor(file, ScriptedRouter.Edit("    p_post();", "    p_post();\n    p_notify();", "p_notify dipanggil."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, _) = await harness.RunAsync("Panggil p_notify setelah simpan");
        Assert.Equal(ThreadStatus.Completed, result.Status);

        await harness.Service<IFunctionCallScanner>().ScanAsync(new FunctionScanRequest(harness.ProjectId, string.Empty, ThreadId: result.ThreadId), CancellationToken.None);

        var sent = Assert.Single(router.CallsTo(ScriptedAgent.FunctionCalls)).LastUser;
        Assert.Contains("## F1 p_save (lines 5-8)", sent);
        Assert.DoesNotContain("p_load", sent);
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
