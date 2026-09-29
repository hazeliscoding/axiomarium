using System.Text.Json.Nodes;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.Core;

public class ClaudeCodeHooksTests
{
    private static TempVault Repo() => new TempVault().Folder("repo/.git").Write("repo/src/app.ts", "export const a = 1;\n");

    private static JsonObject Handler(string command, string? condition = null)
    {
        var handler = new JsonObject { ["type"] = "command", ["command"] = command };
        if (condition is not null)
        {
            handler["if"] = condition;
        }

        return handler;
    }

    private static JsonObject Group(string? matcher, params JsonObject[] handlers)
    {
        var group = new JsonObject { ["hooks"] = new JsonArray([.. handlers]) };
        if (matcher is not null)
        {
            group["matcher"] = matcher;
        }

        return group;
    }

    private static string Settings(params (string Event, JsonObject Group)[] hooks)
    {
        var events = new JsonObject();
        foreach (var (name, group) in hooks)
        {
            if (events[name] is not JsonArray list)
            {
                events[name] = list = [];
            }

            list.Add(group);
        }

        return new JsonObject { ["hooks"] = events }.ToJsonString();
    }

    private static Resolution Resolve(TempVault vault, string launch = "repo", string target = "repo/src/app.ts") =>
        ClaudeCodeModel.Resolve(Path.Combine(vault.Root, launch), Path.Combine(vault.Root, target), TestMachine.For(vault.Root));

    private static (HookMoment, string Handler, bool Runs, string Rule)[] Moments(Resolution resolution) =>
        [.. resolution.Hooks.Select(hook => (hook.Moment, hook.Hook.Handler, hook.Runs, hook.Rule.Id))];

    [Fact]
    public void Hooks_at_each_moment_come_from_managed_user_project_and_local_settings()
    {
        using var vault = Repo()
            .Write("managed/managed-settings.json", Settings(("SessionStart", Group(null, Handler("managed-start")))))
            .Write("home/.claude/settings.json", Settings(("SessionStart", Group("startup", Handler("user-start"))), ("PostToolUse", Group("Edit|Write", Handler("user-after")))))
            .Write("repo/.claude/settings.json", Settings(("PreToolUse", Group("^Ed.*", Handler("project-before"))), ("PreToolUse", Group("Bash", Handler("project-bash")))))
            .Write("repo/.claude/settings.local.json", Settings(("SessionStart", Group("resume", Handler("local-resume"))), ("PostToolUse", Group("Read", Handler("local-read")))));

        Assert.Equal(
            [
                (HookMoment.SessionStart, "managed-start", true, "claude-code/managed-hook"),
                (HookMoment.SessionStart, "user-start", true, "claude-code/user-hook"),
                (HookMoment.BeforeEdit, "project-before", true, "claude-code/project-hook"),
                (HookMoment.AfterEdit, "user-after", true, "claude-code/user-hook"),
            ],
            Moments(Resolve(vault)));
    }

    [Fact]
    public void Project_settings_are_read_from_the_launch_directory_only()
    {
        using var vault = Repo()
            .Write("repo/.claude/settings.json", Settings(("SessionStart", Group(null, Handler("root-start")))))
            .Write("repo/src/.claude/settings.json", Settings(("SessionStart", Group(null, Handler("src-start")))));

        Assert.Equal(["src-start"], Resolve(vault, launch: "repo/src").Hooks.Select(hook => hook.Hook.Handler));
    }

    // Recorded in skills-and-hooks: if scopes an edit hook to a path, anchored at the launch directory.
    [Fact]
    public void An_if_condition_decides_whether_an_edit_hook_runs_for_the_file()
    {
        using var vault = Repo()
            .Write("repo/.claude/settings.json", Settings(
                ("PostToolUse", Group("Edit|Write", Handler("src", "Edit(src/**)"))),
                ("PostToolUse", Group("Edit|Write", Handler("docs", "Edit(docs/**)"))),
                ("PostToolUse", Group("Edit|Write", Handler("any-ts", "Edit(*.ts)"))),
                ("PostToolUse", Group("Edit|Write", Handler("bash-only", "Bash(npm *)")))));

        Assert.Equal(
            [
                (HookMoment.AfterEdit, "src", true, "claude-code/project-hook"),
                (HookMoment.AfterEdit, "docs", false, "claude-code/hook-if-no-match"),
                (HookMoment.AfterEdit, "any-ts", true, "claude-code/project-hook"),
                (HookMoment.AfterEdit, "bash-only", false, "claude-code/hook-if-no-match"),
            ],
            Moments(Resolve(vault)));
    }

    // Recorded in the spike: if on an event that isn't a tool event stops the hook, and a handler in two
    // settings files runs once.
    [Fact]
    public void A_hook_with_if_off_a_tool_event_never_runs_and_a_repeated_handler_runs_once()
    {
        using var vault = Repo()
            .Write("home/.claude/settings.json", Settings(("SessionStart", Group("startup", Handler("shared")))))
            .Write("repo/.claude/settings.json", Settings(
                ("SessionStart", Group(null, Handler("shared"))),
                ("SessionStart", Group(null, Handler("with-if", "Edit(src/**)")))));

        var resolution = Resolve(vault);

        Assert.Equal(
            [
                (HookMoment.SessionStart, "shared", true, "claude-code/user-hook"),
                (HookMoment.SessionStart, "shared", false, "claude-code/hook-duplicate"),
                (HookMoment.SessionStart, "with-if", false, "claude-code/hook-if-ignored"),
            ],
            Moments(resolution));
        Assert.Equal("claude-code/hook-if-ignored", resolution.ConfiguredHooks.Single(hook => hook.Handler == "with-if").Blocked!.Id);
    }

    [Fact]
    public void An_edit_is_an_Edit_of_a_file_that_exists_a_Write_of_one_that_does_not_and_a_NotebookEdit_of_a_notebook()
    {
        using var vault = Repo()
            .Write("repo/nb.ipynb", "{}\n")
            .Write("repo/.claude/settings.json", Settings(
                ("PreToolUse", Group("Edit", Handler("edit"))),
                ("PreToolUse", Group("Write", Handler("write"))),
                ("PreToolUse", Group("NotebookEdit", Handler("notebook")))));

        Assert.Equal(["edit"], Resolve(vault).Hooks.Select(hook => hook.Hook.Handler));
        Assert.Equal(["write"], Resolve(vault, target: "repo/src/new.ts").Hooks.Select(hook => hook.Hook.Handler));
        Assert.Equal(["notebook"], Resolve(vault, target: "repo/nb.ipynb").Hooks.Select(hook => hook.Hook.Handler));
    }

    [Fact]
    public void Settings_can_turn_hooks_off_or_leave_only_the_managed_ones()
    {
        using var vault = Repo()
            .Write("managed/managed-settings.json", Settings(("SessionStart", Group(null, Handler("managed")))))
            .Write("repo/.claude/settings.json", Settings(("SessionStart", Group(null, Handler("project")))));
        vault.Write("home/.claude/settings.json", """{ "disableAllHooks": true }""");

        Assert.Equal(
            [(HookMoment.SessionStart, "managed", true, "claude-code/managed-hook"), (HookMoment.SessionStart, "project", false, "claude-code/hooks-disabled")],
            Moments(Resolve(vault)));

        vault.Write("home/.claude/settings.json", "{}").Write(
            "managed/managed-settings.json",
            new JsonObject { ["allowManagedHooksOnly"] = true, ["hooks"] = JsonNode.Parse(Settings(("SessionStart", Group(null, Handler("managed")))))!["hooks"]!.DeepClone() }.ToJsonString());
        Assert.Equal(
            [(HookMoment.SessionStart, "managed", true, "claude-code/managed-hook"), (HookMoment.SessionStart, "project", false, "claude-code/managed-hooks-only")],
            Moments(Resolve(vault)));
    }

    [Fact]
    public void A_matcher_that_is_not_a_valid_regex_never_runs()
    {
        using var vault = Repo()
            .Write("repo/.claude/settings.json", Settings(("PreToolUse", Group("Edit(", Handler("broken")))));

        var resolution = Resolve(vault);

        Assert.Empty(resolution.Hooks);
        Assert.Equal("claude-code/hook-matcher-invalid", resolution.ConfiguredHooks.Single().Blocked!.Id);
    }

    [Fact]
    public void An_enabled_plugins_hooks_run_too()
    {
        using var vault = Repo()
            .Write("home/.claude/plugins/cache/market/guard/1.0.0/hooks/hooks.json", Settings(("SessionStart", Group(null, Handler("plugin-start")))));
        vault.Write("home/.claude/plugins/installed_plugins.json", new JsonObject
        {
            ["version"] = 2,
            ["plugins"] = new JsonObject
            {
                ["guard@market"] = new JsonArray(new JsonObject { ["installPath"] = Path.Combine(vault.Root, "home", ".claude", "plugins", "cache", "market", "guard", "1.0.0") }),
            },
        }.ToJsonString());
        vault.Write("home/.claude/settings.json", """{ "enabledPlugins": { "guard@market": true } }""");

        Assert.Equal([(HookMoment.SessionStart, "plugin-start", true, "claude-code/plugin-hook")], Moments(Resolve(vault)));
    }

    [Fact]
    public void The_inventory_holds_every_hook_on_every_event_with_where_it_comes_from()
    {
        using var vault = Repo()
            .Write("repo/.claude/settings.json", Settings(("Stop", Group(null, Handler("stop"))), ("PreToolUse", Group("Bash", Handler("bash")))));

        var hooks = Resolve(vault).ConfiguredHooks;

        Assert.Equal([("Stop", "stop"), ("PreToolUse", "bash")], hooks.Select(hook => (hook.Event, hook.Handler)).OrderByDescending(pair => pair.Event));
        Assert.All(hooks, hook => Assert.Equal("repo/.claude/settings.json", TestMachine.Relative(vault.Root, hook.Path)));
        Assert.All(hooks, hook => Assert.Null(hook.Blocked));
    }
}
