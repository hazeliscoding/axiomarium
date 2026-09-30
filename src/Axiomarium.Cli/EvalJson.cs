using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>A harness an eval run ran on.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="Version">What its <c>--version</c> printed.</param>
/// <param name="Model">The model it ran, where known.</param>
/// <param name="Asked">The model <c>axm</c> asked it for: <c>--model</c>, or the one the user chose, or <see langword="null"/> for the harness's default.</param>
internal sealed record EvalHarness(Harness Harness, string Version, string? Model, string? Asked);

/// <summary>Everything <c>axm eval run</c> reports, which both output formats and the saved history render.</summary>
/// <param name="Date">When the run finished.</param>
/// <param name="Axm">The version of <c>axm</c> that ran it.</param>
/// <param name="Harnesses">The harnesses it ran on.</param>
/// <param name="Skipped">Each harness it couldn't run on, and why.</param>
/// <param name="Warmup">The unscored Codex warm-up, when there was one.</param>
/// <param name="Leftovers">Folders a stopped run left behind, which this run removed.</param>
/// <param name="Notes">Assets left out, and why.</param>
/// <param name="Results">Every session's result.</param>
/// <param name="AssetHashes">Each asset's content hash, by its folder.</param>
/// <param name="CaseHashes">Each case's content hash, by its absolute folder.</param>
internal sealed record EvalReport(
    DateTimeOffset Date,
    string Axm,
    IReadOnlyList<EvalHarness> Harnesses,
    IReadOnlyDictionary<Harness, string> Skipped,
    EvalWarmup? Warmup,
    IReadOnlyList<string> Leftovers,
    IReadOnlyList<string> Notes,
    IReadOnlyList<EvalSessionResult> Results,
    IReadOnlyDictionary<string, string> AssetHashes,
    IReadOnlyDictionary<string, string> CaseHashes)
{
    /// <summary>The harness whose model graded the rubrics, or <see langword="null"/> when none was graded.</summary>
    public Harness? Judge { get; init; }

    /// <summary>The command that ran: <c>eval run</c>, or <c>eval compare</c> for each version of a compare.</summary>
    public string Command { get; init; } = "eval run";

    /// <summary>For a compare, which version this is: <c>baseline</c> or <c>candidate</c>. Otherwise <see langword="null"/>.</summary>
    public string? Variant { get; init; }

    /// <summary>For a compare's baseline, the git ref it ran, or <c>none</c>. Otherwise <see langword="null"/>.</summary>
    public string? Ref { get; init; }
}

/// <summary>
/// The JSON <c>axm eval run --json</c> prints, and the history it saves in <c>.axm/evals/</c>. Its shape is a contract,
/// versioned by <c>schemaVersion</c>, and changes only through a decision in ROADMAP.md.
/// </summary>
internal static class EvalJson
{
    /// <summary>The version of the JSON's shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Writes the run: when and with what it ran, then each asset's cases on each harness with their counts, the
    /// median and range of each measure, and every session with its checks, loads, commands and reply.
    /// </summary>
    /// <param name="output">Where to write. The JSON is indented and ends with a newline.</param>
    /// <param name="report">The run.</param>
    /// <param name="asset">Only this asset's cases, by folder, as its history keeps them, or <see langword="null"/> for every asset.</param>
    public static void Write(TextWriter output, EvalReport report, string? asset = null)
    {
        var results = report.Results.Where(result => asset is null || result.Spec.Asset.Folder == asset).ToList();
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("command", report.Command);
            if (report.Variant is not null)
            {
                writer.WriteString("variant", report.Variant);
                writer.WriteString("ref", report.Ref);
            }

            writer.WriteString("axm", report.Axm);
            writer.WriteString("date", report.Date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("home", "sealed");
            writer.WriteString("judge", report.Judge?.Name());
            writer.WriteStartArray("harnesses");
            foreach (var harness in report.Harnesses)
            {
                writer.WriteStartObject();
                writer.WriteString("harness", harness.Harness.Name());
                writer.WriteString("version", harness.Version);
                writer.WriteString("model", harness.Model);
                writer.WriteString("asked", harness.Asked);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("skipped");
            foreach (var (harness, reason) in report.Skipped.OrderBy(item => item.Key))
            {
                writer.WriteStartObject();
                writer.WriteString("harness", harness.Name());
                writer.WriteString("reason", reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (report.Warmup is { } warmup)
            {
                writer.WriteStartObject("warmup");
                writer.WriteString("harness", Harness.Codex.Name());
                writer.WriteNumber("seconds", Math.Round(warmup.Elapsed.TotalSeconds, 1));
                WriteTokens(writer, warmup.Tokens);
                writer.WriteString("stopped", warmup.Problem);
                writer.WriteEndObject();
            }

            WriteStrings(writer, "leftovers", report.Leftovers);
            WriteStrings(writer, "notes", report.Notes);
            writer.WriteStartArray("assets");
            foreach (var group in results.GroupBy(result => result.Spec.Asset.Folder))
            {
                var first = group.First().Spec.Asset;
                writer.WriteStartObject();
                writer.WriteString("asset", first.Folder);
                writer.WriteString("version", first.Manifest?.Version);
                writer.WriteString("hash", report.AssetHashes.GetValueOrDefault(first.Folder));
                writer.WriteStartArray("cases");
                foreach (var summary in EvalSummaries.Summarize([.. group]))
                {
                    WriteCase(writer, summary, [.. group.Where(result => result.Spec.Case == summary.Case && result.Spec.Harness == summary.Harness)], report.CaseHashes);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        output.Write('\n');
    }

    private static void WriteCase(Utf8JsonWriter writer, CaseSummary summary, List<EvalSessionResult> runs, IReadOnlyDictionary<string, string> caseHashes)
    {
        writer.WriteStartObject();
        writer.WriteString("case", summary.Case.Name);
        writer.WriteString("type", EvalCases.Folder(summary.Case.Type));
        writer.WriteString("hash", caseHashes.GetValueOrDefault(runs[0].Spec.CaseFolder));
        writer.WriteString("harness", summary.Harness.Name());
        writer.WriteNumber("passed", summary.Passed);
        writer.WriteNumber("runs", summary.Runs);
        writer.WriteStartObject("stats");
        WriteSpread(writer, "inputTokens", summary.Input);
        WriteSpread(writer, "cachedTokens", summary.Cached);
        WriteSpread(writer, "outputTokens", summary.Output);
        WriteSpread(writer, "seconds", summary.Seconds);
        WriteSpread(writer, "toolCalls", summary.ToolCalls);
        WriteSpread(writer, "turns", summary.Turns);
        WriteSpread(writer, "cost", summary.Cost);
        writer.WriteEndObject();
        if (summary.Slowest is { } slowest)
        {
            writer.WriteStartObject("slowestCall");
            writer.WriteNumber("run", slowest.Run);
            writer.WriteString("what", slowest.Call.What);
            writer.WriteNumber("seconds", Math.Round(slowest.Call.Seconds, 1));
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("slowestCall");
        }

        if (summary.Judge is { } judged)
        {
            writer.WriteStartObject("judge");
            writer.WriteNumber("passed", judged.Passed);
            writer.WriteNumber("judged", judged.Judged);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("judge");
        }
        writer.WriteStartArray("sessions");
        foreach (var run in runs.OrderBy(run => run.Spec.Run))
        {
            WriteSession(writer, run);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteSession(Utf8JsonWriter writer, EvalSessionResult run)
    {
        var record = run.Record;
        writer.WriteStartObject();
        writer.WriteNumber("run", run.Spec.Run);
        writer.WriteBoolean("passed", run.Passed);
        writer.WriteString("stopped", run.Stopped);
        writer.WriteNumber("seconds", Math.Round(run.Elapsed.TotalSeconds, 1));
        writer.WriteString("model", record?.Model);
        WriteTokens(writer, record?.Tokens);
        if (record is null)
        {
            writer.WriteNull("toolCalls");
        }
        else
        {
            writer.WriteNumber("toolCalls", record.ToolCalls);
        }

        if (record?.Turns is { } turns)
        {
            writer.WriteNumber("turns", turns);
        }
        else
        {
            writer.WriteNull("turns");
        }

        if (record?.Cost is { } cost)
        {
            writer.WriteNumber("cost", Math.Round(cost, 4));
        }
        else
        {
            writer.WriteNull("cost");
        }

        writer.WriteNumber("denials", record?.Denials ?? 0);
        WriteStrings(writer, "loads", record?.Activity.Loads ?? []);
        WriteStrings(writer, "commands", record?.Activity.Commands ?? []);
        writer.WriteString("reply", record?.Activity.Reply);
        WriteStrings(writer, "outside", run.Outside);
        if (run.Judged is { } verdict)
        {
            writer.WriteStartObject("judge");
            writer.WriteBoolean("passed", verdict.Passed);
            writer.WriteString("reason", verdict.Reason);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("judge");
        }

        writer.WriteString("judgeProblem", run.JudgeProblem);
        writer.WriteStartArray("calls");
        foreach (var call in record?.Calls ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("what", call.What);
            writer.WriteNumber("seconds", Math.Round(call.Seconds, 1));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("checks");
        foreach (var check in run.Checks)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", check.Check.Kind.ToString().ToLowerInvariant());
            writer.WriteString("expects", EvalChecks.Describe(check.Check));
            writer.WriteBoolean("passed", check.Passed);
            writer.WriteString("observed", check.Observed);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteTokens(Utf8JsonWriter writer, TokenCount? tokens)
    {
        if (tokens is null)
        {
            writer.WriteNull("tokens");
            return;
        }

        writer.WriteStartObject("tokens");
        writer.WriteNumber("input", tokens.Input);
        writer.WriteNumber("cached", tokens.Cached);
        writer.WriteNumber("output", tokens.Output);
        writer.WriteEndObject();
    }

    private static void WriteSpread(Utf8JsonWriter writer, string name, Spread? spread)
    {
        if (spread is null)
        {
            writer.WriteNull(name);
            return;
        }

        writer.WriteStartObject(name);
        writer.WriteNumber("median", Math.Round(spread.Median, 4));
        writer.WriteNumber("min", Math.Round(spread.Min, 4));
        writer.WriteNumber("max", Math.Round(spread.Max, 4));
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}
