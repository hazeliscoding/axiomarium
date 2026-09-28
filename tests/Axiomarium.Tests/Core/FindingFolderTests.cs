using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

// Each finding's folder in findings/ is its documentation and its proof: fires/ is the positive control,
// clean/ shows the fix, and both are tiny repos with a fake home.
public class FindingFolderTests
{
    public static TheoryData<string> Ids => new(InstructionFindings.Ids);

    private static string Folder(string id) => Path.Combine(RepoRoot.Path, "findings", id);

    private static IReadOnlyList<InstructionFinding> Check(string id, string fixture)
    {
        var root = Path.Combine(Folder(id), "fixtures", fixture);
        Assert.True(Directory.Exists(Path.Combine(root, "repo")), $"findings/{id} has no {fixture} fixture.");
        return InstructionFindings.ForRepo(Path.Combine(root, "repo"), TestMachine.For(root));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_fires_fixture_produces_only_its_finding(string id)
    {
        var findings = Check(id, "fires");

        Assert.NotEmpty(findings);
        Assert.All(findings, finding => Assert.Equal(id, finding.Id));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_clean_fixture_produces_nothing(string id)
    {
        Assert.Empty(Check(id, "clean"));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void Finding_md_has_its_four_sections_and_links_its_source(string id)
    {
        var text = File.ReadAllText(Path.Combine(Folder(id), "finding.md")).Replace("\r\n", "\n");

        Assert.StartsWith($"# {id}\n", text);
        foreach (var section in new[] { "What happens", "Why it matters", "Fix", "Source" })
        {
            Assert.Contains($"\n## {section}\n", text);
        }

        Assert.Matches(@"\]\(https://", text[text.IndexOf("\n## Source\n", StringComparison.Ordinal)..]);
    }

    [Fact]
    public void Every_folder_in_findings_is_a_known_finding()
    {
        var folders = Directory.GetDirectories(Path.Combine(RepoRoot.Path, "findings")).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        Assert.Equal(InstructionFindings.Ids.Order(StringComparer.Ordinal), folders);
    }
}
