using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class CodexHookListTests
{
    private const string Run = "/tmp/axm-ground-truth/basic";

    private static JsonObject Hook(string file, string label, string eventName, string? matcher, string trust) => new()
    {
        ["key"] = $"{Run}/{file}:{eventName}:0:0",
        ["eventName"] = eventName,
        ["handlerType"] = "command",
        ["command"] = $"echo MARKER {file} {label}",
        ["matcher"] = matcher,
        ["sourcePath"] = $"{Run}/{file}",
        ["source"] = "user",
        ["isManaged"] = false,
        ["currentHash"] = "sha256:abc",
        ["trustStatus"] = trust,
    };

    // The answer to a hooks/list request, as `codex app-server` sends it.
    private static string Response(JsonArray hooks, params string[] warnings) => new JsonObject
    {
        ["id"] = 2,
        ["result"] = new JsonObject
        {
            ["data"] = new JsonArray
            {
                new JsonObject
                {
                    ["cwd"] = $"{Run}/repo",
                    ["hooks"] = hooks,
                    ["warnings"] = new JsonArray([.. warnings.Select(warning => JsonValue.Create(warning))]),
                    ["errors"] = new JsonArray(),
                },
            },
        },
    }.ToJsonString();

    [Fact]
    public void Hooks_are_named_by_their_marker_with_their_event_matcher_trust_and_hash()
    {
        var response = Response(new JsonArray
        {
            Hook("home/.codex/hooks.json", "after-edit", "postToolUse", "apply_patch", "untrusted"),
            Hook("repo/.codex/hooks.json", "start", "sessionStart", null, "trusted"),
        });

        var (hooks, warnings) = CodexHookList.Parse(response, Run);

        Assert.Equal(
            [
                new CodexHook("home/.codex/hooks.json", "after-edit", "postToolUse", "apply_patch", "untrusted", "sha256:abc", Enabled: true),
                new CodexHook("repo/.codex/hooks.json", "start", "sessionStart", null, "trusted", "sha256:abc", Enabled: true),
            ],
            hooks);
        Assert.Empty(warnings);
    }

    [Fact]
    public void A_hook_the_user_turned_off_says_so()
    {
        var hook = Hook("home/.codex/hooks.json", "off", "sessionStart", null, "trusted");
        hook["enabled"] = false;

        var (hooks, _) = CodexHookList.Parse(Response(new JsonArray { hook }), Run);

        Assert.False(Assert.Single(hooks).Enabled);
    }

    [Fact]
    public void Warnings_keep_their_text_with_paths_made_relative_to_the_run()
    {
        var response = Response([], $"skipping prompt hook in {Run}/home/.codex/hooks.json: prompt hooks are not supported yet");

        var (_, warnings) = CodexHookList.Parse(response, Run);

        Assert.Equal(["skipping prompt hook in home/.codex/hooks.json: prompt hooks are not supported yet"], warnings);
    }

    [Fact]
    public void A_hook_without_its_marker_is_an_error_that_leaves_out_its_command()
    {
        var hook = Hook("home/.codex/hooks.json", "x", "preToolUse", null, "untrusted");
        hook["command"] = "private-tool --secret";

        var problem = Assert.Throws<GroundTruthException>(() => CodexHookList.Parse(Response(new JsonArray { hook }), Run));

        Assert.Contains("home/.codex/hooks.json", problem.Message);
        Assert.DoesNotContain("private-tool", problem.Message);
    }
}
