using System.Text.Json.Nodes;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class CodexHooksTests
{
    private static TempVault Repo() => new TempVault().Folder("repo/.git").Folder("home/.codex").Write("repo/src/app.ts", "export const a = 1;\n");

    private static string HooksJson(params (string Event, string? Matcher, string Command)[] hooks)
    {
        var events = new JsonObject();
        foreach (var (name, matcher, command) in hooks)
        {
            if (events[name] is not JsonArray list)
            {
                events[name] = list = [];
            }

            var group = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command }) };
            if (matcher is not null)
            {
                group["matcher"] = matcher;
            }

            list.Add(group);
        }

        return new JsonObject { ["hooks"] = events }.ToJsonString();
    }

    private static Resolution Resolve(TempVault vault, string launch = "repo") =>
        CodexModel.Resolve(Path.Combine(vault.Root, launch), Path.Combine(vault.Root, "repo", "src", "app.ts"), TestMachine.For(vault.Root));

    // The hashes CI recorded from Codex 0.156.1 for these two hooks, in scenarios/skills-and-hooks.
    [Fact]
    public void A_hooks_trust_hash_is_the_one_codex_computes()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", HooksJson(
            ("PostToolUse", "apply_patch", "echo MARKER home/.codex/hooks.json after-edit"),
            ("SessionStart", "startup", "echo MARKER home/.codex/hooks.json session-start")));

        Assert.Equal(
            [
                "sha256:8a53dcd76c15d30381e5da92e23c27cb7db272023e66f720d8d140c9ba934f14",
                "sha256:16cd69abb7459ea04618376afd10da7b06c0365fb2d2b118de955a7e6e79e4c4",
            ],
            Resolve(vault).ConfiguredHooks.Select(hook => hook.Hash));
    }

    [Fact]
    public void A_hook_runs_only_once_its_hash_is_trusted_and_it_is_not_disabled()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", HooksJson(
            ("SessionStart", null, "new"),
            ("SessionStart", null, "trusted"),
            ("SessionStart", null, "changed"),
            ("SessionStart", null, "off")));
        var hooks = Resolve(vault).ConfiguredHooks;
        string Key(int group) => Path.Combine(vault.Root, "home", ".codex", "hooks.json") + $":session_start:{group}:0";
        vault.Write("home/.codex/config.toml", $"""
            [hooks.state.'{Key(1)}']
            trusted_hash = "{hooks[1].Hash}"

            [hooks.state.'{Key(2)}']
            trusted_hash = "sha256:0000"

            [hooks.state.'{Key(3)}']
            trusted_hash = "{hooks[3].Hash}"
            enabled = false
            """);

        var resolution = Resolve(vault);

        Assert.Equal(["untrusted", "trusted", "modified", "trusted"], resolution.ConfiguredHooks.Select(hook => hook.Trust));
        Assert.Equal(
            [("new", false, "codex/hook-untrusted"), ("trusted", true, "codex/user-hook"), ("changed", false, "codex/hook-modified"), ("off", false, "codex/hook-disabled")],
            resolution.Hooks.Select(hook => (hook.Hook.Handler, hook.Runs, hook.Rule.Id)));
    }

    // Codex keys a hook by its file's full path in the platform's form, then the event, group and index, and matches
    // the key exactly: hooks/list on Windows trusted only the key with backslashes and the same case (0.156.1, 2026-10-04).
    [Fact]
    public void A_trust_entry_counts_only_when_its_key_is_exactly_the_one_codex_writes()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", HooksJson(("SessionStart", null, "a")));
        var hash = Assert.Single(Resolve(vault).ConfiguredHooks).Hash;
        var key = Path.Combine(vault.Root, "home", ".codex", "hooks.json") + ":session_start:0:0";
        var otherSlashes = key.Replace(Path.DirectorySeparatorChar, Path.DirectorySeparatorChar == '\\' ? '/' : '\\');
        string Trust(string entry)
        {
            vault.Write("home/.codex/config.toml", $"[hooks.state.'{entry}']\ntrusted_hash = \"{hash}\"\n");
            return Assert.Single(Resolve(vault).ConfiguredHooks).Trust!;
        }

        Assert.Equal(["trusted", "untrusted", "untrusted"], [Trust(key), Trust(otherSlashes), Trust(key.ToUpperInvariant())]);
    }

    // On Windows a CODEX_HOME written with forward slashes still gives a key with backslashes, as Codex writes it.
    [Fact]
    public void A_hooks_key_has_the_platforms_slashes_however_codex_home_is_written()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", HooksJson(("SessionStart", null, "a")));
        var machine = TestMachine.For(vault.Root) with { CodexHome = Path.Combine(vault.Root, "home", ".codex").Replace('\\', '/') };
        string Trust(string key)
        {
            var hash = Assert.Single(CodexModel.Resolve(Path.Combine(vault.Root, "repo"), Path.Combine(vault.Root, "repo", "src", "app.ts"), machine).ConfiguredHooks).Hash;
            vault.Write("home/.codex/config.toml", $"[hooks.state.'{key}']\ntrusted_hash = \"{hash}\"\n");
            return Assert.Single(CodexModel.Resolve(Path.Combine(vault.Root, "repo"), Path.Combine(vault.Root, "repo", "src", "app.ts"), machine).ConfiguredHooks).Trust!;
        }

        var native = Path.Combine(vault.Root, "home", ".codex", "hooks.json") + ":session_start:0:0";
        Assert.Equal(("trusted", OperatingSystem.IsWindows() ? "untrusted" : "trusted"), (Trust(native), Trust(native.Replace('\\', '/'))));
    }

    // Codex also looks a project up by its folder's canonical path, links followed (codex-rs/config, 0.156.1).
    [Fact]
    public void A_project_trust_entry_may_name_the_folder_a_link_leads_to()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("A symbolic link on Windows needs a privilege the test can't count on.");
        }

        using var vault = Repo().Write("repo/.codex/hooks.json", HooksJson(("SessionStart", null, "project")));
        var link = Path.Combine(vault.Root, "link");
        Directory.CreateSymbolicLink(link, Path.Combine(vault.Root, "repo"));
        vault.Write("home/.codex/config.toml", $"[projects.'{Paths.Canonical(Path.Combine(vault.Root, "repo"))}']\ntrust_level = \"trusted\"\n");

        var hook = Assert.Single(CodexModel.Resolve(link, Path.Combine(link, "src", "app.ts"), TestMachine.For(vault.Root)).Hooks);

        Assert.NotEqual("codex/hook-project-untrusted", hook.Rule.Id);
    }

    // A project's entry in [projects] counts only with its path as Codex writes it, though on Windows in any case:
    // hooks/list on Windows ignored the entry with forward slashes or a trailing backslash (0.156.1, 2026-10-04).
    [Fact]
    public void A_project_trust_entry_counts_only_with_the_path_codex_writes()
    {
        using var vault = Repo().Write("repo/.codex/hooks.json", HooksJson(("SessionStart", null, "project")));
        var repo = Path.Combine(vault.Root, "repo");
        var otherSlashes = repo.Replace(Path.DirectorySeparatorChar, Path.DirectorySeparatorChar == '\\' ? '/' : '\\');
        bool Loads(string entry)
        {
            vault.Write("home/.codex/config.toml", $"[projects.'{entry}']\ntrust_level = \"trusted\"\n");
            return Assert.Single(Resolve(vault).Hooks).Rule.Id != "codex/hook-project-untrusted";
        }

        Assert.Equal(
            [true, false, false, OperatingSystem.IsWindows()],
            [Loads(repo), Loads(otherSlashes), Loads(repo + Path.DirectorySeparatorChar), Loads(repo.ToUpperInvariant())]);
    }

    [Fact]
    public void Admin_hooks_are_managed_and_run_without_trust()
    {
        using var vault = Repo().Write("codex-admin/hooks.json", HooksJson(("SessionStart", "startup", "admin")));

        var hook = Assert.Single(Resolve(vault).Hooks);

        Assert.Equal(("admin", true, "codex/admin-hook", "managed"), (hook.Hook.Handler, hook.Runs, hook.Rule.Id, hook.Hook.Trust));
    }

    // Recorded in the spike: a project's hooks load only once the project is trusted.
    [Fact]
    public void A_projects_hooks_load_only_when_the_project_is_trusted()
    {
        using var vault = Repo().Write("repo/.codex/hooks.json", HooksJson(("SessionStart", null, "project")));

        var untrusted = Assert.Single(Resolve(vault).Hooks);
        Assert.Equal((false, "codex/hook-project-untrusted"), (untrusted.Runs, untrusted.Rule.Id));

        vault.Write("home/.codex/config.toml", $"[projects.'{Path.Combine(vault.Root, "repo")}']\ntrust_level = \"trusted\"\n");
        var trusted = Assert.Single(Resolve(vault).Hooks);
        Assert.Equal((false, "codex/hook-untrusted", "codex/project-hook"), (trusted.Runs, trusted.Rule.Id, trusted.Hook.Source.Id));
    }

    // Codex runs apply_patch hooks for every edit, and also matches them as Write and Edit.
    [Fact]
    public void An_edit_hook_matches_apply_patch_or_its_aliases_and_runs_on_every_edit()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", HooksJson(
            ("PreToolUse", "apply_patch", "patch"),
            ("PreToolUse", "Edit|Write", "alias"),
            ("PostToolUse", "^apply.*", "regex"),
            ("PostToolUse", "Bash", "shell"),
            ("SessionStart", "resume", "resumed")));

        Assert.Equal(
            [(HookMoment.BeforeEdit, "patch"), (HookMoment.BeforeEdit, "alias"), (HookMoment.AfterEdit, "regex")],
            Resolve(vault).Hooks.Select(hook => (hook.Moment, hook.Hook.Handler)));
    }

    [Fact]
    public void A_prompt_handler_is_skipped()
    {
        using var vault = Repo().Write("home/.codex/hooks.json", """{ "hooks": { "SessionStart": [ { "hooks": [ { "type": "prompt", "prompt": "hi" } ] } ] } }""");

        var hook = Assert.Single(Resolve(vault).ConfiguredHooks);

        Assert.Equal("codex/hook-handler-skipped", hook.Blocked!.Id);
    }

    [Fact]
    public void Hooks_in_config_toml_count_too()
    {
        using var vault = Repo().Write("home/.codex/config.toml", "[[hooks.SessionStart]]\nmatcher = \"startup\"\n\n[[hooks.SessionStart.hooks]]\ntype = \"command\"\ncommand = \"from-toml\"\n");

        Assert.Equal(["from-toml"], Resolve(vault).ConfiguredHooks.Select(hook => hook.Handler));
    }
}
