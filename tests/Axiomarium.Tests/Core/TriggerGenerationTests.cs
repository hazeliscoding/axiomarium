using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class TriggerGenerationTests
{
    private static readonly PromptSource Source = new("claude-code", "claude-opus-5-5", "2026-09-28");

    private static string Manifest(string name, string description, string useWhen) =>
        TempVault.Manifest("skill", name)
            .Replace("description: Reviews a codebase for decisions an LLM shouldn't own.", $"description: {description}")
            .Replace("use_when: Writing or changing an asset.", $"use_when: {useWhen}");

    [Fact]
    public void A_vault_skills_rivals_are_the_listed_skills_that_overlap_it_most_with_the_text_the_model_sees()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Folder("home/.codex")
            .Write("repo/skills/deploy/asset.yaml", Manifest("deploy", "Deploys the shop to production.", "Releasing the shop."))
            .Write("repo/skills/deploy/skill.md", "# Deploy\n")
            .Write("repo/.claude/skills/ship/SKILL.md", "---\ndescription: Ships the shop to production.\n---\nSteps.\n")
            .Write("repo/.claude/skills/tables/SKILL.md", "---\ndescription: Formats markdown tables.\n---\nSteps.\n");
        var report = TriggerOverlap.Check(Path.Combine(vault.Root, "repo"), TestMachine.For(vault.Root)).Report!;

        Assert.Equal([("ship", "Ships the shop to production.")], TriggerOverlap.Rivals(report, "deploy"));
        Assert.Empty(TriggerOverlap.Rivals(report, "tables"));
    }

    [Fact]
    public void The_brief_shows_the_skill_and_its_rivals_as_listed_and_asks_for_each_kind_of_prompt()
    {
        var brief = GenerationBrief.Build("deploy", "Deploys the shop. - Releasing the shop.", [("ship", "Ships the shop to production.")]);

        Assert.Contains("\n- deploy: Deploys the shop. - Releasing the shop.\n", brief);
        Assert.Contains("\n- ship: Ships the shop to production.\n", brief);
        Assert.Contains("Write 20 prompts", brief);
        Assert.Contains("- 3 ambiguous:", brief);
        Assert.Contains("""{"prompts": [""", brief);
    }

    [Fact]
    public void Without_rivals_the_brief_asks_for_no_ambiguous_prompts()
    {
        var brief = GenerationBrief.Build("deploy", "Deploys the shop.", []);

        Assert.Contains("Write 17 prompts", brief);
        Assert.DoesNotContain("ambiguous", brief);
        Assert.DoesNotContain("could be confused with", brief);
    }

    [Fact]
    public void A_retry_says_what_was_wrong_with_the_last_answer()
    {
        var brief = GenerationBrief.Build("deploy", "Deploys the shop.", [], previousProblem: "the answer isn't JSON");

        Assert.EndsWith("Your previous answer couldn't be used: the answer isn't JSON. Answer again with only the JSON.\n", brief);
    }

    [Fact]
    public void An_answer_in_a_code_fence_reads_as_a_prompt_file_labeled_with_its_source()
    {
        var read = TriggerPrompts.FromAnswer(
            "Here you go:\n```json\n{\"prompts\": [{\"prompt\": \"Ship it.\", \"kind\": \"positive\", \"should_trigger\": true}]}\n```\n", "deploy", Source);

        Assert.Empty(read.Problems);
        Assert.Equal(("deploy", Source), (read.File!.Skill, read.File.Generated));
        Assert.Equal([new TriggerPrompt("Ship it.", PromptKind.Positive, true)], read.File.Prompts);
    }

    [Fact]
    public void An_answer_that_is_not_json_or_breaks_the_schema_is_a_problem()
    {
        var notJson = TriggerPrompts.FromAnswer("I can't help with that.", "deploy", Source);
        var badKind = TriggerPrompts.FromAnswer("""{"prompts": [{"prompt": "Ship it.", "kind": "sideways", "should_trigger": true}]}""", "deploy", Source);

        Assert.Equal(["the answer isn't JSON"], notJson.Problems.Select(problem => problem.Message));
        Assert.Equal(["Unknown kind: \"sideways\""], badKind.Problems.Select(problem => problem.Message));
        Assert.Null(badKind.File);
    }

    [Fact]
    public void A_written_file_names_its_schema_says_a_model_wrote_it_and_reads_back_the_same()
    {
        var file = new TriggerPromptFile(
            "deploy",
            Source,
            [new TriggerPrompt("Ship \"v2\" today.", PromptKind.Positive, true), new TriggerPrompt("Which one ships it?", PromptKind.Ambiguous, false, "tools:ship")]);

        var text = TriggerPrompts.Write(file);

        Assert.Equal(
            """
            # yaml-language-server: $schema=../../../../schemas/trigger-prompts.schema.json
            # A model wrote these prompts. Review them before you rely on them.
            skill: deploy
            generated:
              by: claude-code
              model: "claude-opus-5-5"
              date: "2026-09-28"
            prompts:
              - prompt: "Ship \"v2\" today."
                kind: positive
                should_trigger: true
              - prompt: "Which one ships it?"
                kind: ambiguous
                should_trigger: false
                rival: "tools:ship"

            """,
            text);
        var read = TriggerPrompts.Read(text, "deploy");
        Assert.Empty(read.Problems);
        Assert.Equal(file.Prompts, read.File!.Prompts);
        Assert.Equal(file.Generated, read.File.Generated);
    }
}
