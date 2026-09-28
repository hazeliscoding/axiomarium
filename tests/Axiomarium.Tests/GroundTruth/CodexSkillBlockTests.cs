using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class CodexSkillBlockTests
{
    private const string Run = "/tmp/axm-ground-truth/basic";

    private static readonly HashSet<string> ScenarioSkills =
    [
        "repo/.agents/skills/deploy/SKILL.md",
        "home/.agents/skills/personal/SKILL.md",
    ];

    // What `codex debug prompt-input` prints: a list of messages, one of them the skills block.
    private static string PromptInput(string? entries, string roots = $"- `r0` = `{Run}/home/.codex/skills/.system`\n- `r1` = `{Run}/repo/.agents/skills`\n- `r2` = `{Run}/home/.agents/skills`")
    {
        var messages = new JsonArray { Message("developer", "You are Codex.") };
        if (entries is not null)
        {
            messages.Add(Message(
                "developer",
                $"<skills_instructions>\n## Skills\nA skill is a set of local instructions.\n### Skill roots\n{roots}\n### Available skills\n{entries}\n</skills_instructions>"));
        }

        return messages.ToJsonString();
    }

    private static JsonObject Message(string role, string text) => new()
    {
        ["type"] = "message",
        ["role"] = role,
        ["content"] = new JsonArray { new JsonObject { ["type"] = "input_text", ["text"] = text } },
    };

    [Fact]
    public void Entries_name_their_scenario_file_in_the_listing_order_and_bundled_skills_only_their_name()
    {
        var entries = string.Join('\n',
            "- imagegen: Generate images. (file: r0/imagegen/SKILL.md)",
            "- deploy: MARKER repo/.agents/skills/deploy/SKILL.md ships (file: r1/deploy/SKILL.md)",
            "- personal: MARKER home/.agents/skills/personal/SKILL.md helps (file: r2/personal/SKILL.md)");

        var listed = CodexSkillBlock.Parse(PromptInput(entries), Run, ScenarioSkills);

        Assert.Equal(
            [
                new ListedSkill("imagegen", null, "whole"),
                new ListedSkill("deploy", "repo/.agents/skills/deploy/SKILL.md", "whole"),
                new ListedSkill("personal", "home/.agents/skills/personal/SKILL.md", "whole"),
            ],
            listed);
    }

    [Fact]
    public void A_description_cut_at_the_cap_ends_in_three_dots()
    {
        var entries = "- deploy: MARKER repo/.agents/skills/deploy/SKILL.md ships the app to... (file: r1/deploy/SKILL.md)";

        Assert.Equal([new ListedSkill("deploy", "repo/.agents/skills/deploy/SKILL.md", "cut")], CodexSkillBlock.Parse(PromptInput(entries), Run, ScenarioSkills));
    }

    // Codex writes a skill's whole path when no root alias makes the list shorter.
    [Fact]
    public void An_entry_can_carry_its_whole_path_instead_of_a_root_alias()
    {
        var entries = $"- deploy: MARKER repo/.agents/skills/deploy/SKILL.md ships (file: {Run}/repo/.agents/skills/deploy/SKILL.md)";

        Assert.Equal([new ListedSkill("deploy", "repo/.agents/skills/deploy/SKILL.md", "whole")], CodexSkillBlock.Parse(PromptInput(entries, roots: ""), Run, ScenarioSkills));
    }

    [Fact]
    public void No_block_means_no_skills()
    {
        Assert.Empty(CodexSkillBlock.Parse(PromptInput(null), Run, ScenarioSkills));
    }

    // Only the path: the skill may be someone's private one.
    [Fact]
    public void A_skill_from_outside_the_run_is_an_error_that_names_only_its_path()
    {
        var entries = "- private: My secret workflow (file: /home/someone/.agents/skills/private/SKILL.md)";

        var problem = Assert.Throws<GroundTruthException>(() => CodexSkillBlock.Parse(PromptInput(entries), Run, ScenarioSkills));

        Assert.Contains("/home/someone/.agents/skills/private/SKILL.md", problem.Message);
        Assert.DoesNotContain("secret", problem.Message);
    }

    [Fact]
    public void A_scenario_skill_without_its_marker_is_an_error()
    {
        var entries = "- deploy: ships the app (file: r1/deploy/SKILL.md)";

        var problem = Assert.Throws<GroundTruthException>(() => CodexSkillBlock.Parse(PromptInput(entries), Run, ScenarioSkills));

        Assert.Contains("repo/.agents/skills/deploy/SKILL.md", problem.Message);
    }
}
