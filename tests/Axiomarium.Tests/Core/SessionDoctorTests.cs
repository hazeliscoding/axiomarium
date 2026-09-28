using System.Text.Json.Nodes;
using Axiomarium.Core.Hooks;

namespace Axiomarium.Tests.Core;

public class SessionDoctorTests
{
    private static string Payload(string cwd, string eventName = "SessionStart")
    {
        var path = Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "ClaudeCode", "session-start.json");
        var payload = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        payload["cwd"] = cwd;
        payload["hook_event_name"] = eventName;
        return payload.ToJsonString();
    }

    private static HookResult Run(TempVault vault, string folder = "repo", string eventName = "SessionStart") =>
        SessionDoctor.Run(Payload(Path.Combine(vault.Root, folder), eventName), _ => TestMachine.For(vault.Root));

    [Fact]
    public void A_healthy_repo_gets_silence()
    {
        using var vault = new TempVault().Folder("repo/.git").Write("repo/CLAUDE.md", "@AGENTS.md\n").Write("repo/AGENTS.md", "agents\n");

        Assert.Equal(new HookResult(null, null), Run(vault));
    }

    [Fact]
    public void Problems_reach_the_user_as_a_summary_and_the_model_in_full()
    {
        using var vault = new TempVault()
            .Folder("repo/.git")
            .Write("repo/CLAUDE.md", "See @docs/testing.md for tests.\n")
            .Write("repo/AGENTS.md", "agents\n")
            .Write("repo/src/app.cs", "class App { }\n");

        var result = Run(vault, "repo/src");

        Assert.Null(result.Problem);
        var output = JsonNode.Parse(result.Output!)!;
        Assert.Equal(
            "axm doctor found 2 warnings in this repo: agents-md-hidden in AGENTS.md, dead-import in CLAUDE.md:1. Run axm doctor to see them.",
            output["systemMessage"]!.GetValue<string>());
        Assert.Equal("SessionStart", output["hookSpecificOutput"]!["hookEventName"]!.GetValue<string>());
        Assert.Equal(
            """
            axm doctor checked this repo when the session started and found problems:
            - WARNING agents-md-hidden: Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code. Fix: Add @AGENTS.md to CLAUDE.md.
            - WARNING dead-import: CLAUDE.md:1 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place. Fix: Restore the file, or remove the import.
            None of this blocks anything. If a file you were meant to read matters to your task, read it yourself. Change instruction files only when the user asks.
            """.ReplaceLineEndings("\n"),
            output["hookSpecificOutput"]!["additionalContext"]!.GetValue<string>());
    }

    [Fact]
    public void Errors_come_first_and_a_long_list_is_cut_short_for_the_user()
    {
        using var vault = new TempVault()
            .Write("repo/axiomarium.yaml", "doctor:\n  skip: []\n")
            .Write("repo/CLAUDE.md", "@docs/a.md\n@docs/b.md\n@docs/c.md\n");

        var output = JsonNode.Parse(Run(vault).Output!)!;

        Assert.Equal(
            "axm doctor found 1 error and 3 warnings in this repo: an error in axiomarium.yaml:2, dead-import in CLAUDE.md:1, dead-import in CLAUDE.md:2, and 1 more. Run axm doctor to see them.",
            output["systemMessage"]!.GetValue<string>());
        Assert.Contains("\n- ERROR axiomarium.yaml:2: Unknown field: doctor.skip\n", output["hookSpecificOutput"]!["additionalContext"]!.GetValue<string>());
    }

    [Fact]
    public void Info_alone_gets_silence()
    {
        using var vault = new TempVault()
            .Write("repo/CLAUDE.md", "@AGENTS.md\n\nAlways run the formatter before you commit any change.\n")
            .Write("repo/AGENTS.md", "Always run the formatter before you commit any change.\n");

        Assert.Equal(new HookResult(null, null), Run(vault));
    }

    [Fact]
    public void Other_events_get_silence()
    {
        using var vault = new TempVault().Write("repo/CLAUDE.md", "See @docs/testing.md for tests.\n");

        Assert.Equal(new HookResult(null, null), Run(vault, eventName: "UserPromptSubmit"));
    }

    [Theory]
    [InlineData("not json", "The hook input isn't valid JSON.")]
    [InlineData("[]", "The hook input isn't a JSON object.")]
    [InlineData("""{"hook_event_name":"SessionStart"}""", "The hook input has no cwd.")]
    public void Unreadable_input_is_a_problem(string input, string problem)
    {
        Assert.Equal(new HookResult(null, problem), SessionDoctor.Run(input, _ => throw new InvalidOperationException("No machine is needed.")));
    }

    [Fact]
    public void A_missing_working_directory_is_a_problem()
    {
        using var vault = new TempVault();

        var result = Run(vault, "nowhere");

        Assert.Null(result.Output);
        Assert.StartsWith("The folder ", result.Problem);
    }
}
