using System.Text.Json.Nodes;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class EvalInstallTests
{
    private const string Axm = @"C:\Program Files\axm\axm.exe";

    private static readonly Dictionary<string, string> Both = new() { ["claude-code"] = "experimental", ["codex"] = "experimental" };

    private static DiscoveredAsset Asset(AssetKind kind, string name, HookBlock? hook = null) =>
        new(kind, name, $"{kind.Folder()}/{name}", null, new AssetManifest("experimental", "0.1.0", Both, "Deploys the shop.", kind == AssetKind.Skill ? "Releasing \"v2\"." : null, hook));

    private static Installation Plan(DiscoveredAsset asset, Harness harness, string? settings = null) =>
        EvalInstall.Plan(asset, "# Body\n", harness, Axm, settings);

    [Fact]
    public void A_skill_is_each_harness_s_skill_file_as_a_trigger_test_writes_it()
    {
        var claude = Assert.Single(Plan(Asset(AssetKind.Skill, "deploy"), Harness.ClaudeCode).Copy);
        var codex = Assert.Single(Plan(Asset(AssetKind.Skill, "deploy"), Harness.Codex).Copy);

        Assert.Equal(
            (".claude/skills/deploy/SKILL.md", "---\nname: deploy\ndescription: \"Deploys the shop.\"\nwhen_to_use: \"Releasing \\\"v2\\\".\"\n---\n# Body\n"),
            (claude.Path, claude.Content));
        Assert.Equal(
            (".agents/skills/deploy/SKILL.md", "---\nname: deploy\ndescription: \"Deploys the shop. - Releasing \\\"v2\\\".\"\n---\n# Body\n"),
            (codex.Path, codex.Content));
    }

    // A hook's command runs the axm that runs the eval, whose path may have spaces, through the shell Claude Code uses.
    [Theory]
    [InlineData("after-edit", "PostToolUse", "Write|Edit|NotebookEdit")]
    [InlineData("before-edit", "PreToolUse", "Write|Edit|NotebookEdit")]
    [InlineData("session-start", "SessionStart", "startup")]
    public void A_hook_is_a_claude_code_settings_entry_that_runs_this_axm(string hookEvent, string claudeEvent, string matcher)
    {
        var file = Assert.Single(Plan(Asset(AssetKind.Hook, "scope-sheriff", new HookBlock(hookEvent, "axm hook scope-sheriff")), Harness.ClaudeCode).Copy);

        Assert.Equal(".claude/settings.json", file.Path);
        var entry = JsonNode.Parse(file.Content)!["hooks"]![claudeEvent]![0]!;
        Assert.Equal(matcher, entry["matcher"]!.GetValue<string>());
        Assert.Equal("\"C:/Program Files/axm/axm.exe\" hook scope-sheriff", entry["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void A_hook_joins_the_case_s_own_settings()
    {
        const string Settings = """{ "permissions": { "deny": ["Read(.env)"] }, "hooks": { "PostToolUse": [{ "matcher": "Bash", "hooks": [{ "type": "command", "command": "echo hi" }] }] } }""";

        var file = Assert.Single(Plan(Asset(AssetKind.Hook, "scope-sheriff", new HookBlock("after-edit", "axm hook scope-sheriff")), Harness.ClaudeCode, Settings).Copy);

        var settings = JsonNode.Parse(file.Content)!;
        Assert.Equal("Read(.env)", settings["permissions"]!["deny"]![0]!.GetValue<string>());
        Assert.Equal(["Bash", "Write|Edit|NotebookEdit"], settings["hooks"]!["PostToolUse"]!.AsArray().Select(entry => entry!["matcher"]!.GetValue<string>()));
    }

    [Fact]
    public void An_agent_is_a_claude_code_subagent_and_a_custom_agent_in_the_sealed_codex_home()
    {
        var claude = Plan(Asset(AssetKind.Agent, "auditor"), Harness.ClaudeCode);
        var codex = Plan(Asset(AssetKind.Agent, "auditor"), Harness.Codex);

        Assert.Equal((".claude/agents/auditor.md", "---\nname: auditor\ndescription: \"Deploys the shop.\"\n---\n# Body\n"), (claude.Copy[0].Path, claude.Copy[0].Content));
        Assert.Empty(codex.Copy);
        Assert.Equal(
            ("agents/auditor.toml", "name = \"auditor\"\ndescription = \"Deploys the shop.\"\ndeveloper_instructions = \"# Body\\n\"\n"),
            (codex.CodexHome[0].Path, codex.CodexHome[0].Content));
    }

    [Fact]
    public void What_axm_eval_can_t_install_says_why()
    {
        Assert.Equal(
            "axm eval runs skills, hooks and agents. A policy's rules are tested through the assets that enforce them.",
            Plan(Asset(AssetKind.Policy, "boundaries"), Harness.ClaudeCode).Problem);
        Assert.Equal(
            "axm eval installs hooks for Claude Code only.",
            Plan(Asset(AssetKind.Hook, "scope-sheriff", new HookBlock("after-edit", "axm hook scope-sheriff")), Harness.Codex).Problem);
    }
}
