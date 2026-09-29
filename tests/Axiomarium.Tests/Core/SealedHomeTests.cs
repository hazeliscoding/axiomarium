using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class SealedHomeTests
{
    private static readonly Harness[] Both = [Harness.ClaudeCode, Harness.Codex];

    // The real home, as a test injects it: a login for each harness, the user's model choices, and a Codex user skill.
    private static TempVault RealHome() => new TempVault()
        .Write("home/.claude/.credentials.json", "{}")
        .Write("home/.claude/settings.json", """{ "model": "opus", "hooks": { "Stop": [] }, "enabledPlugins": { "superpowers@official": true } }""")
        .Write("home/.codex/auth.json", "{}")
        .Write("home/.codex/config.toml", "model = \"gpt-6-sol\"\nmodel_reasoning_effort = \"high\"\n\n[windows]\nsandbox = \"elevated\"\n\n[mcp_servers.docs]\nurl = \"https://example.com\"\n")
        .Write("home/.agents/skills/herdr/SKILL.md", "---\nname: herdr\n---\n")
        .Write("home/.agents/skills/synced/abc/pdf/SKILL.md", "---\nname: pdf\n---\n");

    private static SealedHome Plan(TempVault real, IReadOnlyCollection<Harness>? harnesses = null, OSPlatform? platform = null) =>
        SealedHomes.Plan(Path.Combine(real.Root, "run", "home"), TestMachine.For(real.Root), harnesses ?? Both, platform ?? OSPlatform.Windows);

    private static string File(SealedHome home, string path) => Assert.Single(home.Files, file => file.Path == path).Content;

    [Fact]
    public void Claude_code_gets_the_login_and_the_model_and_nothing_else_of_the_user_s_setup()
    {
        using var real = RealHome();

        var home = Plan(real);

        Assert.Null(home.Problem);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"model":"opus","syncClaudeAiSkills":false}"""), JsonNode.Parse(File(home, ".claude/settings.json"))));
        Assert.Contains((Path.Combine(real.Root, "home", ".claude", ".credentials.json"), ".claude/.credentials.json"), home.Logins);
    }

    // On Windows Codex reads the real ~/.agents/skills whatever the environment says, so each skill there is turned off.
    [Fact]
    public void Codex_gets_the_login_the_model_and_its_sandbox_with_the_user_s_skills_plugins_and_apps_off()
    {
        using var real = RealHome();

        var config = File(Plan(real), ".codex/config.toml");

        var skills = Path.Combine(real.Root, "home", ".agents", "skills");
        Assert.Equal(
            "model = \"gpt-6-sol\"\nmodel_reasoning_effort = \"high\"\n\n[features]\nplugins = false\nremote_plugin = false\napps = false\n\n[windows]\nsandbox = \"elevated\"\n\n"
            + $"[[skills.config]]\npath = {Toml(Path.Combine(skills, "herdr", "SKILL.md"))}\nenabled = false\n\n"
            + $"[[skills.config]]\npath = {Toml(Path.Combine(skills, "synced", "abc", "pdf", "SKILL.md"))}\nenabled = false\n",
            config);
        Assert.Contains((Path.Combine(real.Root, "home", ".codex", "auth.json"), ".codex/auth.json"), Plan(real).Logins);
    }

    [Fact]
    public void The_environment_points_both_harnesses_and_the_home_at_the_fake_one()
    {
        using var real = RealHome();

        var home = Plan(real);

        var folder = Path.Combine(real.Root, "run", "home");
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = Path.Combine(folder, ".claude"),
                ["CODEX_HOME"] = Path.Combine(folder, ".codex"),
                ["HOME"] = folder,
                ["USERPROFILE"] = folder,
            },
            home.Environment);
    }

    [Fact]
    public void A_harness_that_isn_t_run_needs_no_login()
    {
        using var real = new TempVault().Write("home/.codex/auth.json", "{}");

        var home = Plan(real, [Harness.Codex]);

        Assert.Null(home.Problem);
        Assert.DoesNotContain(home.Files, file => file.Path.StartsWith(".claude/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_login_managed_skills_and_macos_s_keychain_each_stop_the_run()
    {
        using var real = RealHome();
        using var noLogin = new TempVault().Write("home/.claude/settings.json", "{}");
        using var managed = RealHome().Write("managed/.claude/skills/audit/SKILL.md", "---\nname: audit\n---\n");

        Assert.Equal(
            ("No Claude Code login in ~/.claude/.credentials.json.", "Log in to Claude Code first."),
            (Plan(noLogin, [Harness.ClaudeCode]).Problem, Plan(noLogin, [Harness.ClaudeCode]).Hint));
        Assert.StartsWith("This machine has managed Claude Code skills", Plan(managed).Problem);
        Assert.Equal(
            "On macOS, Claude Code keeps its login in the Keychain, and axm eval can't borrow it for a sealed home yet.",
            Plan(real, [Harness.ClaudeCode], OSPlatform.OSX).Problem);
        Assert.Null(Plan(real, [Harness.Codex], OSPlatform.OSX).Problem);
    }

    private static string Toml(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";
}
