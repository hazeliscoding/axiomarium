using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>
/// The JSON <c>axm eval compare --json</c> prints. Its shape is a contract, versioned by <c>schemaVersion</c>, and
/// changes only through a decision in ROADMAP.md. Each version's runs are also saved in the <c>axm eval run</c> shape.
/// </summary>
internal static class EvalCompareJson
{
    /// <summary>The version of the JSON's shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Writes the compare: both versions, the harnesses, and each case on each harness with its two sides.</summary>
    /// <param name="output">Where to write. The JSON is indented and ends with a newline.</param>
    /// <param name="report">The compare.</param>
    public static void Write(TextWriter output, CompareReport report)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("command", "eval compare");
            writer.WriteString("axm", report.Axm);
            writer.WriteString("date", report.Date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("home", "sealed");
            writer.WriteString("asset", report.Asset);
            writer.WriteStartObject("baseline");
            writer.WriteString("ref", report.Ref);
            writer.WriteString("hash", report.BaselineHash);
            writer.WriteString("reused", report.Reused);
            writer.WriteEndObject();
            writer.WriteStartObject("candidate");
            writer.WriteString("hash", report.CandidateHash);
            writer.WriteEndObject();
            writer.WriteBoolean("casesChanged", report.CasesChanged);
            writer.WriteNumber("runs", report.Runs);
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
            writer.WriteStartArray("notes");
            foreach (var note in report.Notes)
            {
                writer.WriteStringValue(note);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("cases");
            foreach (var compared in report.Cases)
            {
                writer.WriteStartObject();
                writer.WriteString("case", compared.Case.Name);
                writer.WriteString("type", EvalCases.Folder(compared.Case.Type));
                writer.WriteString("harness", compared.Harness.Name());
                WriteSide(writer, "baseline", compared.Baseline);
                WriteSide(writer, "candidate", compared.Candidate);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        output.Write('\n');
    }

    private static void WriteSide(Utf8JsonWriter writer, string name, CaseSide side)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("passed", side.Passed);
        writer.WriteNumber("runs", side.Runs);
        writer.WriteStartObject("stats");
        WriteSpread(writer, "inputTokens", side.Input);
        WriteSpread(writer, "cachedTokens", side.Cached);
        WriteSpread(writer, "outputTokens", side.Output);
        WriteSpread(writer, "seconds", side.Seconds);
        WriteSpread(writer, "toolCalls", side.ToolCalls);
        WriteSpread(writer, "turns", side.Turns);
        WriteSpread(writer, "cost", side.Cost);
        writer.WriteEndObject();
        if (side.Judge is { } judge)
        {
            writer.WriteStartObject("judge");
            writer.WriteNumber("passed", judge.Passed);
            writer.WriteNumber("judged", judge.Judged);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("judge");
        }

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
}
