using System.Text.Json.Nodes;
using Axiomarium.Core.Hooks;

namespace Axiomarium.Tests.Core;

public class ScopeSheriffTests
{
    // Fixtures are real PostToolUse payloads from Claude Code 2.1.283 with placeholder paths. Each test
    // points cwd and the edited path at a temporary repo, so paths work on every platform.
    private static string Payload(string fixture, string cwd, string file)
    {
        var path = Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "ClaudeCode", fixture);
        var payload = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        payload["cwd"] = cwd;
        var input = payload["tool_input"]!.AsObject();
        input[input.ContainsKey("notebook_path") ? "notebook_path" : "file_path"] = file;
        return payload.ToJsonString();
    }

    private static string Edit(TempVault repo, string relativeFile, string? cwd = null) =>
        Payload("post-tool-use-edit.json", cwd ?? repo.Root, Path.Combine(repo.Root, relativeFile));

    private static string? Warning(HookResult result)
    {
        Assert.Null(result.Problem);
        if (result.Output is null)
        {
            return null;
        }

        var output = JsonNode.Parse(result.Output)!["hookSpecificOutput"]!;
        Assert.Equal("PostToolUse", output["hookEventName"]!.GetValue<string>());
        return output["additionalContext"]!.GetValue<string>();
    }

    [Fact]
    public void Without_a_scope_file_it_is_silent()
    {
        using var repo = new TempVault().Folder("src");

        Assert.Null(Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt"))));
    }

    [Fact]
    public void An_edit_inside_the_scope_is_silent()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/billing/**\n");

        Assert.Null(Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt"))));
    }

    [Theory]
    [InlineData("post-tool-use-write.json", "src/billing/notes.txt")]
    [InlineData("post-tool-use-edit.json", "src/billing/invoice.txt")]
    [InlineData("post-tool-use-notebookedit.json", "analysis.ipynb")]
    public void An_edit_outside_the_scope_warns_the_agent(string fixture, string file)
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n");

        var warning = Warning(ScopeSheriff.Run(Payload(fixture, repo.Root, Path.Combine(repo.Root, file))));

        Assert.Equal(
            $"{file} is outside this task's scope (src/api/**). In your reply to the user, say why this edit was needed. If the task grew, add the path to .axm/scope.",
            warning);
    }

    [Fact]
    public void The_scope_is_found_from_a_subfolder()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n").Folder("src/api");

        var warning = Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt", cwd: Path.Combine(repo.Root, "src", "api"))));

        Assert.StartsWith("src/billing/invoice.txt is outside", warning);
    }

    [Fact]
    public void Every_pattern_is_listed()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\ntests/api/**\n");

        Assert.Contains("(src/api/**, tests/api/**)", Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt"))));
    }

    [Fact]
    public void Comments_blank_lines_and_a_leading_dot_slash_are_ignored()
    {
        using var repo = new TempVault().Write(".axm/scope", "# Task: the orders API\n\n./src/api/**\n");

        Assert.Null(Warning(ScopeSheriff.Run(Edit(repo, "src/api/orders.cs"))));
        Assert.Contains("(src/api/**)", Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt"))));
    }

    [Fact]
    public void A_scope_file_without_patterns_declares_no_scope()
    {
        using var repo = new TempVault().Write(".axm/scope", "# nothing yet\n");

        Assert.Null(Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt"))));
    }

    [Fact]
    public void An_edit_outside_the_repo_shows_its_full_path()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n");
        using var elsewhere = new TempVault();
        var file = Path.Combine(elsewhere.Root, "notes.txt");

        var warning = Warning(ScopeSheriff.Run(Payload("post-tool-use-write.json", repo.Root, file)));

        Assert.StartsWith($"{file} is outside this task's scope", warning);
    }

    [Fact]
    public void An_invalid_scope_line_is_named_in_the_warning()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/{api\nsrc/web/**\n");

        var warning = Warning(ScopeSheriff.Run(Edit(repo, "src/billing/invoice.txt")));

        Assert.Equal(
            "src/billing/invoice.txt is outside this task's scope (src/web/**). In your reply to the user, say why this edit was needed. If the task grew, add the path to .axm/scope. "
            + "Line 1 of .axm/scope matches nothing: '{' at column 5 is never closed.",
            warning);
    }

    [Fact]
    public void A_scope_with_only_invalid_lines_still_declares_a_scope()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/{api\n");

        Assert.StartsWith(
            "src/api/orders.cs is outside this task's scope (no valid patterns).",
            Warning(ScopeSheriff.Run(Edit(repo, "src/api/orders.cs"))));
    }

    [Theory]
    [InlineData("hook_event_name", "PreToolUse")]
    [InlineData("tool_name", "Bash")]
    public void Other_events_and_tools_are_silent(string field, string value)
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n");
        var payload = JsonNode.Parse(Edit(repo, "src/billing/invoice.txt"))!.AsObject();
        payload[field] = value;

        Assert.Null(Warning(ScopeSheriff.Run(payload.ToJsonString())));
    }

    [Theory]
    [InlineData("not json", "The hook input isn't valid JSON.")]
    [InlineData("[]", "The hook input isn't a JSON object.")]
    [InlineData("{}", "The hook input has no hook_event_name.")]
    [InlineData("""{"hook_event_name":"PostToolUse","tool_name":"Edit","cwd":"x","tool_input":{}}""", "The Edit input has no file_path.")]
    public void Malformed_input_is_a_problem(string input, string expected)
    {
        var result = ScopeSheriff.Run(input);

        Assert.Null(result.Output);
        Assert.Equal(expected, result.Problem);
    }
}
