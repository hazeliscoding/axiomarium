using System.Text.Json.Nodes;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

/// <summary>
/// Replays scripted harness sessions and records what was asked, so no test runs a model. It answers in call
/// order, or by <c>respond</c> when sessions run in parallel and their order isn't fixed.
/// </summary>
internal sealed class FakeRunner : IHarnessRunner
{
    private readonly Queue<HarnessOutput> _outputs = new();
    private readonly Func<HarnessCall, HarnessOutput>? _respond;
    private readonly List<HarnessCall> _calls = [];
    private int _folders;

    public FakeRunner(params HarnessOutput[] outputs)
    {
        foreach (var output in outputs)
        {
            _outputs.Enqueue(output);
        }
    }

    public FakeRunner(Func<HarnessCall, HarnessOutput> respond) => _respond = respond;

    /// <summary>Where <see cref="CreateFolder"/> makes folders: the test's own folder, never the real scratch root.</summary>
    public string? FolderRoot { get; init; }

    public IReadOnlyList<HarnessCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public Task<HarnessOutput> RunAsync(HarnessCall call, CancellationToken cancellation = default)
    {
        lock (_calls)
        {
            _calls.Add(call);
            return Task.FromResult(_respond?.Invoke(call) ?? _outputs.Dequeue());
        }
    }

    public string CreateFolder() =>
        Directory.CreateDirectory(Path.Combine(FolderRoot ?? throw new InvalidOperationException("Set FolderRoot to the test's own folder."), $"workspace-{Interlocked.Increment(ref _folders)}")).FullName;

    /// <summary>A Claude Code session that answers in one turn of text, shaped like the spike's fixture.</summary>
    public static HarnessOutput ClaudeAnswer(string text, string model = "claude-opus-5-5") => new(
        true,
        [
            new JsonObject { ["type"] = "system", ["subtype"] = "init", ["model"] = model, ["claude_code_version"] = "2.1.284", ["skills"] = new JsonArray() }.ToJsonString(),
            new JsonObject
            {
                ["type"] = "assistant",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) },
            }.ToJsonString(),
            new JsonObject { ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = text }.ToJsonString(),
        ],
        0,
        "");

    /// <summary>A Claude Code session that loads <paramref name="skills"/>, then runs a command, where it's stopped.</summary>
    public static HarnessOutput ClaudePick(params string[] skills) => new(
        true,
        [
            new JsonObject { ["type"] = "system", ["subtype"] = "init", ["model"] = "claude-opus-5-5", ["claude_code_version"] = "2.1.284", ["skills"] = new JsonArray() }.ToJsonString(),
            .. skills.Select(skill => Assistant(new JsonObject { ["type"] = "tool_use", ["name"] = "Skill", ["input"] = new JsonObject { ["skill"] = skill } })),
            Assistant(new JsonObject { ["type"] = "tool_use", ["name"] = "Bash", ["input"] = new JsonObject { ["command"] = "git status" } }),
        ],
        null,
        "");

    /// <summary>A Codex session whose first command reads <paramref name="skills"/> from the repo's <c>.agents/skills</c>, where it's stopped.</summary>
    public static HarnessOutput CodexRead(params string[] skills)
    {
        var command = skills.Length == 0 ? "rg --files" : string.Join(" && ", skills.Select(skill => $"cat .agents/skills/{skill}/SKILL.md"));
        JsonObject Item(string type) =>
            new() { ["type"] = type, ["item"] = new JsonObject { ["id"] = "item_1", ["type"] = "command_execution", ["command"] = command, ["status"] = "completed" } };
        // Reading a skill doesn't end the loads, so the turn's end does, as it would once Codex answered.
        return new(
            true,
            [
                new JsonObject { ["type"] = "thread.started" }.ToJsonString(), Item("item.started").ToJsonString(), Item("item.completed").ToJsonString(),
                new JsonObject { ["type"] = "turn.completed" }.ToJsonString(),
            ],
            null,
            "");
    }

    /// <summary>What each harness prints for <c>--version</c>.</summary>
    public static HarnessOutput Version(HarnessCall call) =>
        new(true, [call.Command == "claude" ? "2.1.284 (Claude Code)" : "codex-cli 0.156.1"], 0, "");

    private static string Assistant(JsonObject block) =>
        new JsonObject { ["type"] = "assistant", ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = new JsonArray(block) } }.ToJsonString();

    /// <summary>A captured stream from the spike, as a session that was stopped at its last line.</summary>
    public static HarnessOutput Fixture(string name) =>
        new(true, File.ReadAllLines(Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "triggers", name)), null, "");
}

/// <summary>A clock stopped at noon UTC on 2026-09-28, in UTC.</summary>
internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
