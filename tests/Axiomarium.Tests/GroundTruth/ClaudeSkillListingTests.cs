using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class ClaudeSkillListingTests
{
    // Claude Code lists a scenario skill by its folder name, and a scenario gives every skill a unique one.
    private static readonly Dictionary<string, string> ScenarioSkills = new()
    {
        ["deploy"] = "repo/.claude/skills/deploy/SKILL.md",
        ["personal"] = "home/.claude/skills/personal/SKILL.md",
        ["nested"] = "repo/sub/.claude/skills/nested/SKILL.md",
        ["legacy"] = "repo/.claude/commands/legacy.md",
    };

    private const string Personal = "- personal: MARKER home/.claude/skills/personal/SKILL.md helps";
    private const string Deploy = "- deploy: MARKER repo/.claude/skills/deploy/SKILL.md ships - when the user says ship";
    private const string Legacy = "- legacy: MARKER repo/.claude/commands/legacy.md old";

    private static string Listing(bool initial, params (string Name, string Entry)[] entries) => new JsonObject
    {
        ["type"] = "attachment",
        ["attachment"] = new JsonObject
        {
            ["type"] = "skill_listing",
            ["content"] = string.Join('\n', entries.Select(entry => entry.Entry)),
            ["skillCount"] = entries.Length,
            ["isInitial"] = initial,
            ["names"] = new JsonArray([.. entries.Select(entry => JsonValue.Create(entry.Name))]),
        },
    }.ToJsonString();

    [Fact]
    public void Launch_entries_name_their_scenario_file_and_keep_the_listing_order()
    {
        var transcript = new[] { Listing(initial: true, ("personal", Personal), ("deploy", Deploy), ("legacy", Legacy)) };

        var (launch, read) = ClaudeSkillListing.Parse(transcript, ScenarioSkills);

        Assert.Equal(
            [
                new ListedSkill("personal", "home/.claude/skills/personal/SKILL.md", "whole", Personal.Length),
                new ListedSkill("deploy", "repo/.claude/skills/deploy/SKILL.md", "whole", Deploy.Length),
                new ListedSkill("legacy", "repo/.claude/commands/legacy.md", "whole", Legacy.Length),
            ],
            launch);
        Assert.Empty(read);
    }

    [Fact]
    public void Skills_listed_after_the_first_listing_were_added_on_read()
    {
        const string nested = "- nested: MARKER repo/sub/.claude/skills/nested/SKILL.md helps (from sub/.claude/skills — applies when working on files under sub/)";
        var transcript = new[] { Listing(initial: true, ("deploy", Deploy)), Listing(initial: false, ("nested", nested)) };

        var (_, read) = ClaudeSkillListing.Parse(transcript, ScenarioSkills);

        Assert.Equal([new ListedSkill("nested", "repo/sub/.claude/skills/nested/SKILL.md", "whole", nested.Length)], read);
    }

    [Fact]
    public void A_cut_description_and_a_name_only_entry_are_told_apart_from_a_whole_one()
    {
        const string cut = "- deploy: MARKER repo/.claude/skills/deploy/SKILL.md ships the app to…";
        var transcript = new[] { Listing(initial: true, ("deploy", cut), ("personal", "- personal")) };

        var (launch, _) = ClaudeSkillListing.Parse(transcript, ScenarioSkills);

        Assert.Equal(
            [
                new ListedSkill("deploy", "repo/.claude/skills/deploy/SKILL.md", "cut", cut.Length),
                new ListedSkill("personal", "home/.claude/skills/personal/SKILL.md", "name-only", "- personal".Length),
            ],
            launch);
    }

    // A built-in skill isn't a file, so only its name and the length of its entry are kept. An entry can
    // run over several lines.
    [Fact]
    public void A_built_in_skill_keeps_its_name_and_length_and_nothing_else()
    {
        const string api = "- claude-api: Reference for the API.\nTRIGGER — read first.\nSKIP otherwise.";
        var transcript = new[] { Listing(initial: true, ("claude-api", api), ("init", "- init")) };

        var (launch, _) = ClaudeSkillListing.Parse(transcript, ScenarioSkills);

        Assert.Equal([new ListedSkill("claude-api", null, "whole", api.Length), new ListedSkill("init", null, "name-only", 6)], launch);
    }

    [Fact]
    public void A_scenario_skill_without_its_marker_is_an_error()
    {
        var transcript = new[] { Listing(initial: true, ("deploy", "- deploy: ships the app")) };

        var problem = Assert.Throws<GroundTruthException>(() => ClaudeSkillListing.Parse(transcript, ScenarioSkills));

        Assert.Contains("deploy", problem.Message);
    }

    [Fact]
    public void A_marker_that_names_a_file_the_scenario_has_no_skill_for_is_an_error()
    {
        var transcript = new[] { Listing(initial: true, ("stray", "- stray: MARKER repo/.claude/skills/stray/SKILL.md")) };

        var problem = Assert.Throws<GroundTruthException>(() => ClaudeSkillListing.Parse(transcript, ScenarioSkills));

        Assert.Contains("repo/.claude/skills/stray/SKILL.md", problem.Message);
    }
}
