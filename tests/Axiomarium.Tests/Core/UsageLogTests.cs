using Axiomarium.Core.Registry;

namespace Axiomarium.Tests.Core;

public class UsageLogTests
{
    [Fact]
    public void Dated_sections_are_entries_with_their_repo_and_linked_assets()
    {
        const string log = """
            # Dogfooding

            Notes on using the vault on real work.

            ## 2026-10-02 · carmine-workbench

            Ran [the auditor](../agents/determinism-auditor/) on billing, with [the policy](../policies/deterministic-boundaries/policy.md).

            ### What it found

            A retry that [the auditor](../agents/determinism-auditor) flagged.

            ## Ideas

            Try [the scope sheriff](../hooks/scope-sheriff).

            ## 2026-10-05 - axiomarium

            [The auditor](../agents/determinism-auditor#usage) again. See the [README](../README.md) and [elsewhere](https://example.com/agents/x).

            """;

        var entries = UsageLog.Parse(log);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new DateOnly(2026, 10, 2), entries[0].Date);
        Assert.Equal("carmine-workbench", entries[0].Repo);
        Assert.Equal(["agents/determinism-auditor", "policies/deterministic-boundaries"], entries[0].Assets);
        Assert.Equal(new DateOnly(2026, 10, 5), entries[1].Date);
        Assert.Equal("axiomarium", entries[1].Repo);
        Assert.Equal(["agents/determinism-auditor"], entries[1].Assets);
    }

    [Fact]
    public void A_heading_without_a_real_date_is_not_an_entry()
    {
        var entries = UsageLog.Parse("## 2026-13-45 · x\n\n[a](../agents/a)\n");

        Assert.Empty(entries);
    }

    [Fact]
    public void Links_resolve_from_the_docs_folder_or_the_root()
    {
        var entries = UsageLog.Parse("## 2026-10-02 · x\n\n[a](agents/a) [b](/skills/b) [c](./../hooks/c/hook.md)\n");

        Assert.Equal(["skills/b", "hooks/c"], Assert.Single(entries).Assets);
    }

    [Fact]
    public void A_date_alone_names_no_repo()
    {
        var entries = UsageLog.Parse("## 2026-10-02\n\n[a](../agents/a)\n");

        Assert.Equal("", Assert.Single(entries).Repo);
    }
}
