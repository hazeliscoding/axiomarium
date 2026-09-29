using Axiomarium.Cli;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Cli;

public class TriggersExportCommandTests
{
    private const string Prompts = """
        skill: deploy
        prompts:
          - prompt: Ship the shop.
            kind: positive
            should_trigger: true

        """;

    private static TempVault Vault(bool prompts = true)
    {
        var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/skills/deploy/asset.yaml", TempVault.Manifest("skill", "deploy"))
            .Write("repo/skills/deploy/skill.md", "# Deploy\n");
        return prompts ? vault.Write("repo/skills/deploy/evals/trigger/prompts.yaml", Prompts) : vault;
    }

    private static (int ExitCode, string Output, string Error) Export(TempVault vault, params string[] flags) =>
        CliRun.Run(["triggers", "export", "deploy", .. flags], machine: TestMachine.For(vault.Root), currentDirectory: Path.Combine(vault.Root, "repo"));

    [Fact]
    public void Prints_the_skill_creator_list_as_is()
    {
        using var vault = Vault();

        var (exitCode, output, error) = Export(vault, "--format", "skill-creator");

        Assert.Equal((AxmCli.Passed, ""), (exitCode, error));
        Assert.Equal(TriggerExport.SkillCreator(TriggerPrompts.Read(Prompts, "deploy").File!), output);
    }

    // The sample manifest supports Claude Code only, so there's no Codex provider.
    [Fact]
    public void Prints_a_promptfoo_config_for_the_harnesses_the_skill_supports()
    {
        using var vault = Vault();

        var (_, output, _) = Export(vault, "--format", "promptfoo");

        Assert.Equal(TriggerExport.Promptfoo(TriggerPrompts.Read(Prompts, "deploy").File!, [Harness.ClaudeCode]), output);
    }

    [Fact]
    public void Needs_a_format_and_prompts()
    {
        using var vault = Vault(prompts: false);

        var (noFormat, _, _) = Export(vault);
        var (noPrompts, _, error) = Export(vault, "--format", "promptfoo");

        Assert.Equal((AxmCli.CouldNotRun, AxmCli.CouldNotRun), (noFormat, noPrompts));
        Assert.StartsWith("axm: deploy has no trigger prompts yet.\n     Run axm triggers generate deploy first.", error);
    }
}
