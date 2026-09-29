using System.Text.Json.Nodes;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Tests.Core;

public class CodexStreamTests
{
    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "triggers", name));

    private static string Command(string command, string type = "item.completed", string id = "item_1") =>
        new JsonObject { ["type"] = type, ["item"] = new JsonObject { ["id"] = id, ["type"] = "command_execution", ["command"] = command, ["status"] = "completed" } }.ToJsonString();

    [Fact]
    public void The_first_command_s_skill_reads_are_the_loads_in_the_order_it_read_them()
    {
        var session = CodexStream.Read(Fixture("codex-two-skills-read.jsonl"), workingDirectory: @"C:\axm-triggers\spike", home: @"C:\Users\dev");

        Assert.Equal(
            [
                @"C:\Users\dev\.codex\plugins\cache\claude-plugins-official\superpowers\6.4.1\skills\using-superpowers\SKILL.md",
                @"C:\axm-triggers\spike\.agents\skills\db-migration\SKILL.md",
            ],
            session.Reads);
        Assert.Equal(["using-superpowers", "db-migration"], session.Loads);
        Assert.Equal("I'll use the repository's db-migration skill.", session.Message);
    }

    // Codex on Linux and macOS reads a skill with sed or cat, from the launch directory or the home folder.
    [Fact]
    public void Relative_and_home_paths_in_a_posix_command_resolve_from_the_launch_directory_and_home()
    {
        var session = CodexStream.Read(
            [Command("/bin/bash -lc \"sed -n '1,200p' .agents/skills/deploy/SKILL.md && cat ~/.agents/skills/ship/SKILL.md\"")],
            workingDirectory: "/tmp/axm-triggers/run",
            home: "/home/dev");

        Assert.Equal(["/tmp/axm-triggers/run/.agents/skills/deploy/SKILL.md", "/home/dev/.agents/skills/ship/SKILL.md"], session.Reads.Select(path => path.Replace('\\', '/')));
        Assert.Equal(["deploy", "ship"], session.Loads);
    }

    // A command that only reads skills is how Codex loads them, like Claude Code's Skill calls, so it doesn't end
    // the loads. The first real run met using-superpowers read on its own, then the skill the task needed.
    [Fact]
    public void Commands_that_only_read_skills_keep_loading_until_one_does_something_else()
    {
        const string alone = "\"C:\\\\Program Files\\\\pwsh.exe\" -Command \"Get-Content -LiteralPath 'C:\\\\Users\\\\dev\\\\.codex\\\\skills\\\\using-superpowers\\\\SKILL.md'\"";
        string[] lines = [Command(alone, id: "item_1"), Command("cat .agents/skills/deploy/SKILL.md && rg --files", id: "item_2"), Command("cat .agents/skills/late/SKILL.md", id: "item_3")];

        var session = CodexStream.Read(lines, "/tmp/run", "/home/dev");

        Assert.Equal(["using-superpowers", "deploy"], session.Loads);
        Assert.Equal([false, true, false], lines.Select(CodexStream.EndsPick));
    }

    [Fact]
    public void Only_the_first_command_counts_and_a_first_command_without_a_skill_loads_nothing()
    {
        var session = CodexStream.Read(
            [Command("rg --files", id: "item_1"), Command("cat .agents/skills/deploy/SKILL.md", id: "item_2")],
            workingDirectory: "/tmp/run",
            home: "/home/dev");

        Assert.Empty(session.Loads);
    }

    [Fact]
    public void The_first_completed_command_or_the_end_of_the_turn_ends_a_pick()
    {
        var lines = Fixture("codex-two-skills-read.jsonl");

        Assert.Equal([false, false, false, false, true, false], lines.Select(CodexStream.EndsPick));
        Assert.True(CodexStream.EndsPick("""{"type":"turn.completed","usage":{}}"""));
        Assert.True(CodexStream.EndsPick("""{"type":"turn.failed","error":{"message":"quota"}}"""));
        Assert.False(CodexStream.EndsPick("2026-09-29T03:36:52Z ERROR rmcp::transport::worker"));
    }

    [Fact]
    public void An_event_with_a_key_twice_still_reads()
    {
        const string read = """{"type":"item.completed","item":{"id":"item_1","id":"item_2","type":"command_execution","command":"cat .agents/skills/deploy/SKILL.md && ls"}}""";

        var session = CodexStream.Read([read], "/tmp/run", "/home/dev");

        Assert.Equal(["deploy"], session.Loads);
        Assert.True(CodexStream.EndsPick(read));
    }

    [Fact]
    public void A_failed_turn_says_why()
    {
        var session = CodexStream.Read(["""{"type":"turn.failed","error":{"message":"You've hit your usage limit."}}"""], "/tmp/run", "/home/dev");

        Assert.Equal("You've hit your usage limit.", session.Error);
        Assert.Empty(session.Loads);
    }
}
