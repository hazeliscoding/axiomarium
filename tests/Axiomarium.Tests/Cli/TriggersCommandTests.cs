using Axiomarium.Cli;
using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class TriggersCommandTests
{
    private static (int ExitCode, string Output, string Error) Triggers(TempVault vault, bool terminal = false, string root = "repo") =>
        CliRun.Run(["triggers", "--root", Path.Combine(vault.Root, root)], terminal: terminal, machine: TestMachine.For(vault.Root));

    private static string Skill(string description) => $"---\ndescription: {description}\n---\nSteps.\n";

    private static TempVault Shop() => new TempVault()
        .Folder("repo/.git")
        .Folder("home/.codex")
        .Write("repo/.claude/skills/deploy/SKILL.md", Skill("Deploys the shop to production."))
        .Write("repo/.claude/skills/tables/SKILL.md", Skill("Formats markdown tables."))
        .Write("home/.claude/skills/ship/SKILL.md", Skill("Ships a release of the shop to production."))
        .Write("repo/.agents/skills/lint/SKILL.md", Skill("Checks spelling."));

    [Fact]
    public void Lists_each_overlapping_pair_with_its_score_the_terms_it_shares_and_both_files()
    {
        using var vault = Shop();

        var (exitCode, output, _) = Triggers(vault);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(
            """
            AXM TRIGGERS // 2 harnesses · 1 overlapping pair

              CLAUDE CODE // 3 skills compared · 13 built-in skills left out, their text isn't recorded
              01  0.21  ship ~ deploy  shares production, shop
                  ~/.claude/skills/ship/SKILL.md
                  .claude/skills/deploy/SKILL.md

              CODEX // 1 skill compared
              --  no overlap

            1 overlapping pair. Shared wording, not proof the agent mixes them up.

            """,
            output);
    }

    [Fact]
    public void Overlap_is_not_a_problem_so_the_summary_gets_the_warnings_face_and_no_overlap_the_all_clear_one()
    {
        using var overlapping = Shop();
        using var clean = new TempVault().Folder("repo/.git").Write("repo/.claude/skills/tables/SKILL.md", Skill("Formats markdown tables."));

        var (_, some, _) = Triggers(overlapping, terminal: true);
        var (_, none, _) = Triggers(clean, terminal: true);

        Assert.Contains(Kaomoji.WarningsOnly, some.Split('\n')[^2]);
        Assert.Contains(Kaomoji.AllClear, none.Split('\n')[^2]);
        Assert.Contains("0 overlapping pairs", none);
    }

    [Fact]
    public void A_missing_folder_exits_2_on_stderr()
    {
        using var vault = new TempVault();

        var (exitCode, output, error) = Triggers(vault, root: "nowhere");

        Assert.Equal((AxmCli.CouldNotRun, ""), (exitCode, output));
        Assert.StartsWith($"axm: The folder {Path.Combine(vault.Root, "nowhere")} does not exist.", error);
    }
}
