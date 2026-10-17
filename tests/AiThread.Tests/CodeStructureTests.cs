using AiThread.Service.Implementations;
using AiThread.Service.Workspace.Structure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

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
        var root = Directory.CreateTempSubdirectory("ait-structure-").FullName;
        try
        {
            Write(root, "Logic/OrderBL.cs", "namespace Shop { public class OrderBL { public void Save() { } } }");
            var projects = new CliTestProjects(root);
            var service = new CodeStructureService(projects, new TestLifetime(), NullLogger<CodeStructureService>.Instance);
            Assert.Equal(1, (await service.GetSummaryAsync(projects.Project.Id, rescan: false)).Types);

            // Threads in two CLIs finish one after the other; the snapshot is still fresh, so only the refresh picks the
            // files up. The second request replaces the first and scans both files.
            Write(root, "Logic/InvoiceBL.cs", "namespace Shop { public class InvoiceBL { public void Post() { } } }");
            service.Refresh(projects.Project.Id);
            Write(root, "Logic/StockBL.cs", "namespace Shop { public class StockBL { public void Move() { } } }");
            service.Refresh(projects.Project.Id);

            var types = 0;
            for (var i = 0; i < 50 && types != 3; i++)
            {
                await Task.Delay(100);
                types = (await service.GetSummaryAsync(projects.Project.Id, rescan: false)).Types;
            }
            Assert.Equal(3, types);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
