using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;

namespace AiThread.Tests;

public class SkillSourceLocatorTests
{
    [Fact]
    public void GitHubRepository_IsDownloadedAsZip()
    {
        var location = SkillSourceLocator.Locate("https://github.com/anthropics/skills/");

        Assert.Equal(SkillSourceKind.GitHub, location.Kind);
        Assert.Equal("https://github.com/anthropics/skills", location.Url);
        Assert.Equal("anthropics/skills", location.Name);
        Assert.Equal("https://api.github.com/repos/anthropics/skills/zipball", location.DownloadUrl);
        Assert.Equal("https://github.com/anthropics/skills.git", location.CloneUrl);
        Assert.Null(location.SubPath);
    }

    [Fact]
    public void GitHubShorthand_IsAccepted()
    {
        var location = SkillSourceLocator.Locate("anthropics/skills");

        Assert.Equal(SkillSourceKind.GitHub, location.Kind);
        Assert.Equal("https://github.com/anthropics/skills", location.Url);
    }

    [Fact]
    public void GitHubTreeLink_KeepsRefAndFolder()
    {
        var location = SkillSourceLocator.Locate("https://github.com/acme/agent-kit/tree/dev/skills/backend");

        Assert.Equal("dev", location.Ref);
        Assert.Equal("skills/backend", location.SubPath);
        Assert.Equal("acme/agent-kit/skills/backend", location.Name);
        Assert.Equal("https://api.github.com/repos/acme/agent-kit/zipball/dev", location.DownloadUrl);
    }

    [Fact]
    public void GitHubBlobOfMarkdown_IsOneRawFile()
    {
        var location = SkillSourceLocator.Locate("https://github.com/acme/kit/blob/main/skills/code-review/SKILL.md");

        Assert.Equal(SkillSourceKind.RawFile, location.Kind);
        Assert.Equal("code-review", location.Name);
        Assert.Equal("https://raw.githubusercontent.com/acme/kit/main/skills/code-review/SKILL.md", location.DownloadUrl);
    }

    [Fact]
    public void GitLabTreeLink_UsesArchiveApi()
    {
        var location = SkillSourceLocator.Locate("https://gitlab.com/group/sub/project/-/tree/main/prompts");

        Assert.Equal(SkillSourceKind.GitLab, location.Kind);
        Assert.Equal("group/sub/project/prompts", location.Name);
        Assert.Equal("prompts", location.SubPath);
        Assert.Equal("https://gitlab.com/api/v4/projects/group%2Fsub%2Fproject/repository/archive.zip?sha=main", location.DownloadUrl);
    }

    [Theory]
    [InlineData("https://git.example.com/team/skills.git")]
    [InlineData("git@git.example.com:team/skills.git")]
    public void OtherGitHosts_AreCloned(string url)
    {
        var location = SkillSourceLocator.Locate(url);

        Assert.Equal(SkillSourceKind.Git, location.Kind);
        Assert.Equal("team/skills", location.Name);
        Assert.NotNull(location.CloneUrl);
    }

    [Fact]
    public void OtherLinks_AreWebPages()
    {
        var location = SkillSourceLocator.Locate("https://example.com/awesome/skills");

        Assert.Equal(SkillSourceKind.Web, location.Kind);
        Assert.Equal("example.com/awesome/skills", location.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://example.com/skills")]
    [InlineData("not a link")]
    public void InvalidLinks_AreRejected(string url)
    {
        Assert.Throws<AppValidationException>(() => SkillSourceLocator.Locate(url));
    }
}

public class SkillArchiveTests
{
    [Fact]
    public void Extract_DropsTopFolderAndSkipsGit()
    {
        using var workspace = new TempWorkspace();
        using var zip = Zip(("acme-kit-abc123/skills/review/SKILL.md", "review"), ("acme-kit-abc123/README.md", "readme"), ("acme-kit-abc123/.git/config", "x"));

        var count = SkillArchive.Extract(zip, workspace.Root, null);

        Assert.Equal(2, count);
        Assert.True(File.Exists(Path.Combine(workspace.Root, "skills", "review", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(workspace.Root, "README.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, ".git")));
    }

    [Fact]
    public void Extract_KeepsOnlySubPath()
    {
        using var workspace = new TempWorkspace();
        using var zip = Zip(("repo-1/skills/review/SKILL.md", "review"), ("repo-1/docs/guide.md", "guide"));

        SkillArchive.Extract(zip, workspace.Root, "skills");

        Assert.True(File.Exists(Path.Combine(workspace.Root, "review", "SKILL.md")));
        Assert.False(Directory.Exists(Path.Combine(workspace.Root, "docs")));
    }

    [Fact]
    public void Extract_MissingSubPath_Throws()
    {
        using var workspace = new TempWorkspace();
        using var zip = Zip(("repo-1/docs/guide.md", "guide"));

        Assert.Throws<InvalidDataException>(() => SkillArchive.Extract(zip, workspace.Root, "skills"));
    }

    [Fact]
    public void Extract_RefusesZipSlip()
    {
        using var workspace = new TempWorkspace();
        using var zip = Zip(("a/SKILL.md", "ok"), ("../evil.txt", "x"));

        Assert.Throws<InvalidDataException>(() => SkillArchive.Extract(zip, workspace.Root, null));
    }

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }
        stream.Position = 0;
        return stream;
    }
}

public class SkillDiscoveryTests
{
    [Fact]
    public void Frontmatter_ReadsFlatAndFoldedValues()
    {
        var document = SkillFrontmatter.Parse("---\r\nname: \"code-review\"\r\ndescription: >\r\n  Reviews pull requests\r\n  for bugs.\r\nmetadata:\r\n  owner: x\r\n---\r\n# Code review\r\nBody.");

        Assert.Equal("code-review", document.Fields["name"]);
        Assert.Equal("Reviews pull requests for bugs.", document.Fields["description"]);
        Assert.False(document.Fields.ContainsKey("owner"));
        Assert.Equal("# Code review\nBody.", document.Body);
    }

    [Fact]
    public void Frontmatter_Missing_KeepsWholeText()
    {
        var document = SkillFrontmatter.Parse("# Title\n\nFirst paragraph here.");

        Assert.Empty(document.Fields);
        Assert.Equal("First paragraph here.", SkillFrontmatter.FirstParagraph(document.Body));
    }

    [Fact]
    public void Scan_FindsEverySkillMd()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("skills/review/SKILL.md", "---\nname: code-review\ndescription: Review code.\n---\nDo reviews.");
        workspace.Write("skills/tdd/skill.md", "# TDD\n\nWrite the test first.");
        workspace.Write("node_modules/x/SKILL.md", "ignored");

        var skills = SkillDiscovery.Scan(workspace.Root, "kit");

        Assert.Equal(2, skills.Count);
        Assert.Equal(("skills/review", "code-review", "Review code."), (skills[0].RelativeDir, skills[0].Name, skills[0].Description));
        Assert.Equal(("skills/tdd", "tdd", "Write the test first."), (skills[1].RelativeDir, skills[1].Name, skills[1].Description));
    }

    [Fact]
    public void FromFile_RefusesPathsOutsideTheRoot()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("agents/backend.md", "You are a backend engineer.");

        Assert.NotNull(SkillDiscovery.FromFile(workspace.Root, "agents/backend.md", "backend", null));
        Assert.Null(SkillDiscovery.FromFile(workspace.Root, "../outside.md", null, null));
        Assert.Null(SkillDiscovery.FromFile(workspace.Root, "agents/missing.md", null, null));
    }

    [Fact]
    public void Slug_IsFolderSafe()
    {
        Assert.Equal("senior-backend-engineer", SkillImporter.Slug("Senior Backend Engineer!"));
        Assert.Equal("skill", SkillImporter.Slug("???"));
    }
}

public class SkillPromptTests
{
    [Fact]
    public void ForNode_WithoutSkills_IsTheOldPrompt()
    {
        var node = new AiNode { SystemPrompt = "Use tabs." };

        Assert.Equal(AgentPrompts.WithNodeInstructions("BASE", "Use tabs."), AgentPrompts.ForNode("BASE", node, 40_000, "C:\\skills"));
    }

    [Fact]
    public void ForNode_AppendsSkillsInOrderAfterInstructions()
    {
        var node = new AiNode { SystemPrompt = "Use tabs." };
        node.Skills.Add(Attached(1, "testing", "Write tests first."));
        node.Skills.Add(Attached(0, "code-review", "Check every diff."));

        var prompt = AgentPrompts.ForNode("BASE", node, 40_000, "C:\\skills");

        var instructions = prompt.IndexOf("Additional instructions:", StringComparison.Ordinal);
        var review = prompt.IndexOf("<skill name=\"code-review\"", StringComparison.Ordinal);
        var testing = prompt.IndexOf("<skill name=\"testing\"", StringComparison.Ordinal);
        Assert.True(instructions > 0 && review > instructions && testing > review);
        Assert.Contains("Check every diff.", prompt);
        Assert.DoesNotContain("read_file", prompt);
    }

    [Fact]
    public void ForNode_TruncatesToTheBudgetAndPointsToTheFile()
    {
        var node = new AiNode();
        node.Skills.Add(Attached(0, "big", new string('x', 5_000)));

        var prompt = AgentPrompts.ForNode("BASE", node, 1_000, "C:\\skills", canReadFiles: true);

        Assert.Contains("[... truncated — the full skill is in", prompt);
        Assert.Contains("SKILL.md", prompt);
        Assert.Contains("read_file", prompt);
        Assert.True(prompt.Length < 2_500);
    }

    private static NodeSkill Attached(int order, string name, string content) => new()
    {
        Order = order,
        Skill = new Skill { Name = name, Description = $"{name} skill", RelativeDir = $"skills/{name}", Content = content, SkillSourceId = Guid.Empty }
    };
}

public class SkillOrchestratorTests
{
    [Fact]
    public void ParseMatches_KeepsValidRankedMatches()
    {
        var json = JsonNode.Parse("""
            {"matches":[
              {"index":2,"score":0.7,"reason":"b"},
              {"index":9,"score":0.9,"reason":"out of range"},
              {"index":1,"score":0.95,"reason":"a"},
              {"index":2,"score":0.8,"reason":"duplicate"},
              {"index":3,"score":0.2,"reason":"too weak"}
            ]}
            """)!;

        var matches = SkillOrchestrator.ParseMatches(json, 3);

        Assert.Equal([1, 2], matches.Select(m => m.Index));
        Assert.Equal("a", matches[0].Reason);
    }

    [Fact]
    public void Prefilter_PrefersKeywordMatches()
    {
        var all = Enumerable.Range(0, 10)
            .Select(i => new SkillCandidate(Guid.NewGuid(), $"skill-{i}", "generic", null, "src"))
            .Append(new SkillCandidate(Guid.NewGuid(), "backend-engineer", "Senior engineer habits", "engineer, backend", "src"))
            .ToList();

        var picked = SkillOrchestrator.Prefilter(all, "aku butuh kamu jadi engineer yang handal", 3);

        Assert.Equal(3, picked.Count);
        Assert.Equal("backend-engineer", picked[0].Name);
    }

    [Fact]
    public void ApplyAnnotations_DropsRejectedAndKeepsUnmentioned()
    {
        var batch = new[]
        {
            new DiscoveredSkill(".", "SKILL.md", "a", "A", "body"),
            new DiscoveredSkill("b", "SKILL.md", "b", "", "body"),
            new DiscoveredSkill("c", "SKILL.md", "c", "C", "body")
        };
        var json = JsonNode.Parse("""{"skills":[{"index":1,"keep":false},{"index":2,"keep":true,"tags":"x, y","description":"Made up."}]}""");

        var result = SkillCatalogAgent.ApplyAnnotations(batch, json);

        Assert.Equal(["b", "c"], result.Select(s => s.Name));
        Assert.Equal("x, y", result[0].Tags);
        Assert.Equal("Made up.", result[0].Description);
        Assert.Null(result[1].Tags);
    }

    [Fact]
    public void ApplyAnnotations_UnreadableAnswer_KeepsEverything()
    {
        var batch = new[] { new DiscoveredSkill(".", "SKILL.md", "a", "A", "body") };

        Assert.Single(SkillCatalogAgent.ApplyAnnotations(batch, null));
    }
}
