using Axiomarium.Core.Health;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerPromptsTests
{
    private const string File = "skills/x/evals/trigger/prompts.yaml";

    private const string Valid = """
        skill: x
        generated:
          by: claude-code
          model: claude-opus-5-5
          date: 2026-09-28
        prompts:
          - prompt: Add a new skill to the vault.
            kind: positive
            should_trigger: true
          - prompt: Which skills does this repo's agent see?
            kind: ambiguous
            should_trigger: false
            rival: explain-skills

        """;

    private static IReadOnlyList<Diagnostic> Doctor(string prompts)
    {
        using var vault = new TempVault().Asset("skills/x", TempVault.Manifest("skill", "x")).Write(File, prompts);
        return Axiomarium.Core.Health.Doctor.Run(vault.Root).Report!.Diagnostics;
    }

    private static (int?, string)[] Problems(string prompts) => [.. Doctor(prompts).Select(diagnostic => (diagnostic.Location?.Line, diagnostic.Message))];

    [Fact]
    public void A_valid_file_reads_as_typed_prompts_and_the_doctor_is_quiet()
    {
        var read = TriggerPrompts.Read(Valid, "x");

        Assert.Empty(read.Problems);
        Assert.Equal(("x", "claude-code", "claude-opus-5-5", "2026-09-28"), (read.File!.Skill, read.File.Generated!.By, read.File.Generated.Model, read.File.Generated.Date));
        Assert.Equal(
            [new TriggerPrompt("Add a new skill to the vault.", PromptKind.Positive, true), new TriggerPrompt("Which skills does this repo's agent see?", PromptKind.Ambiguous, false, "explain-skills")],
            read.File.Prompts);
        Assert.Empty(Doctor(Valid));
    }

    [Fact]
    public void The_doctor_checks_the_file_against_its_schema_at_the_line_at_fault()
    {
        var diagnostic = Assert.Single(Doctor("skill: x\nprompts:\n  - prompt: Write an asset.\n    kind: sideways\n    should_trigger: true\n"));

        Assert.Equal((File, 4, "Unknown kind: \"sideways\""), (diagnostic.File, diagnostic.Location?.Line, diagnostic.Message));
        Assert.Equal(Severity.Error, diagnostic.Severity);
    }

    [Fact]
    public void An_empty_list_of_prompts_is_not_trigger_evals()
    {
        Assert.Equal([(2, "prompts needs at least 1 item")], Problems("skill: x\nprompts: []\n"));
    }

    // A kind that contradicts should_trigger would turn a hit into a miss in every score.
    [Fact]
    public void The_skill_must_match_its_folder_and_each_prompt_must_agree_with_its_kind()
    {
        var problems = Problems("""
            skill: y
            prompts:
              - prompt: One.
                kind: positive
                should_trigger: false
              - prompt: Two.
                kind: negative
                should_trigger: true
              - prompt: Three.
                kind: adversarial
                should_trigger: false
                rival: z

            """);

        Assert.Equal(
            [
                (1, "skill \"y\" doesn't match its folder \"x\""),
                (5, "prompts[0] is a positive prompt, so should_trigger must be true"),
                (8, "prompts[1] is a negative prompt, so should_trigger must be false"),
                (12, "prompts[2] names a rival, which only an ambiguous prompt does"),
            ],
            problems);
    }

    [Fact]
    public void A_file_that_is_not_yaml_is_an_error_and_other_kinds_of_asset_are_not_checked()
    {
        using var agent = new TempVault().Asset("agents/y", TempVault.Manifest("agent", "y")).Write("agents/y/evals/trigger/prompts.yaml", "not: [valid\n");

        var diagnostic = Assert.Single(Doctor("skill: x\nprompts: [unclosed\n"));

        Assert.Equal(File, diagnostic.File);
        Assert.Empty(Axiomarium.Core.Health.Doctor.Run(agent.Root).Report!.Diagnostics);
    }
}
