using System.Text.RegularExpressions;
using Axiomarium.Core.Instructions;
using Axiomarium.Tests.Cli;

namespace Axiomarium.Tests.Core;

// The docs describe the CLI and the findings, so these keep them from drifting: links resolve, and every
// command and finding has its place.
public partial class DocsTests
{
    private static string Docs => Path.Combine(RepoRoot.Path, "docs");

    public static TheoryData<string> Pages =>
        new([.. Directory.GetFiles(Docs, "*.md").Select(path => Path.GetRelativePath(RepoRoot.Path, path).Replace('\\', '/')).Order(StringComparer.Ordinal), "README.md"]);

    // Relative Markdown links outside code, without their #anchor, that point nowhere.
    private static IEnumerable<string> BrokenLinks(string markdown, string folder) =>
        Link().Matches(Fence().Replace(markdown, "")).Select(match => match.Groups[1].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith('#') && !target.StartsWith("mailto:", StringComparison.Ordinal))
            .Where(target => !Path.Exists(Path.Combine(folder, target.Split('#')[0])));

    // Every command axm's own help lists, such as "triggers generate".
    private static List<string> Commands()
    {
        using var vault = new TempVault();
        List<string> Listed(params string[] command) =>
            [.. CliRun.Run([.. command, "--help"], machine: TestMachine.For(vault.Root)).Output
                .Split('\n').SkipWhile(line => line != "Commands:").Skip(1).TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
                .Select(line => line.Trim().Split(' ')[0])];
        return [.. Listed().SelectMany(command => Listed(command).Select(sub => $"{command} {sub}").Prepend(command))];
    }

    [Fact]
    public void A_broken_link_is_found_and_a_good_one_is_not()
    {
        using var vault = new TempVault().Write("docs/cli.md", "# CLI\n");

        var broken = BrokenLinks("See [the CLI](cli.md#exit-codes), [nothing](missing.md), [the web](https://example.com) and `[code](gone.md)`.\n```\n[fenced](gone.md)\n```\n", Path.Combine(vault.Root, "docs"));

        Assert.Equal(["missing.md"], broken);
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_relative_link_resolves(string page)
    {
        var path = Path.Combine(RepoRoot.Path, page);

        Assert.Empty(BrokenLinks(File.ReadAllText(path), Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void Every_command_is_in_the_cli_page()
    {
        var commands = Commands();
        var page = File.ReadAllText(Path.Combine(Docs, "cli.md"));

        Assert.Contains("triggers generate", commands);
        Assert.Contains("hook session-doctor", commands);
        Assert.Contains(commands, command => !"`axm doctor`".Contains($"`axm {command}", StringComparison.Ordinal));
        Assert.DoesNotContain(commands, command => !page.Contains($"`axm {command}", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_finding_is_linked_from_the_findings_page()
    {
        var page = File.ReadAllText(Path.Combine(Docs, "findings.md"));

        Assert.Contains(InstructionFindings.Ids, id => !"[`dead-import`](../findings/dead-import/finding.md)".Contains($"[`{id}`](../findings/{id}/finding.md)", StringComparison.Ordinal));
        Assert.DoesNotContain(InstructionFindings.Ids, id => !page.Contains($"[`{id}`](../findings/{id}/finding.md)", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\[[^\]]*\]\(([^)\s]+)\)")]
    private static partial Regex Link();

    // Fenced blocks and inline code, where a link is an example rather than a link.
    [GeneratedRegex(@"```.*?```|`[^`\n]*`", RegexOptions.Singleline)]
    private static partial Regex Fence();
}
