using System.Text.Json.Nodes;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

/// <summary>Replays scripted harness sessions in order, and records what was asked, so no test runs a model.</summary>
internal sealed class FakeRunner(params HarnessOutput[] outputs) : IHarnessRunner
{
    private readonly Queue<HarnessOutput> _outputs = new(outputs);

    public List<HarnessCall> Calls { get; } = [];

    public Task<HarnessOutput> RunAsync(HarnessCall call, CancellationToken cancellation = default)
    {
        Calls.Add(call);
        return Task.FromResult(_outputs.Dequeue());
    }

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
}

/// <summary>A clock stopped at noon UTC on 2026-09-28, in UTC.</summary>
internal sealed class FixedClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
