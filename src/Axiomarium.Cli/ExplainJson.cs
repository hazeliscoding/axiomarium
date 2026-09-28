using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>
/// The JSON <c>axm explain --json</c> prints. Its shape is a contract, versioned by <c>schemaVersion</c>, and
/// changes only through a decision in ROADMAP.md.
/// </summary>
public static class ExplainJson
{
    /// <summary>The version of the JSON's shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    /// Writes the explanation: the target and launch directory, each harness's loaded and dropped files, the
    /// findings, and every rule the files cite, by id. Paths are shown the way the text report shows them.
    /// </summary>
    /// <param name="output">Where to write. The JSON is indented and ends with a newline.</param>
    /// <param name="explanation">What each harness loads.</param>
    /// <param name="findings">The findings in <paramref name="explanation"/>, in the order to list them.</param>
    /// <param name="home">The user's home folder, shown as <c>~</c>.</param>
    public static void Write(TextWriter output, Explanation explanation, IReadOnlyList<InstructionFinding> findings, string home)
    {
        string Show(string path) => DisplayPath.Of(path, explanation.RepoRoot, home);
        var rules = new Dictionary<string, HarnessRule>();

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("target", Show(explanation.Target));
            writer.WriteString("launchDirectory", Show(explanation.Launch));
            writer.WriteStartArray("harnesses");
            foreach (var harness in explanation.Harnesses)
            {
                writer.WriteStartObject();
                writer.WriteString("harness", harness.Harness.Name());
                writer.WriteString("confirmedWith", harness.ConfirmedWith);
                writer.WriteStartArray("loaded");
                foreach (var item in harness.Resolution.Loaded)
                {
                    rules.TryAdd(item.Rule.Id, item.Rule);
                    writer.WriteStartObject();
                    writer.WriteString("path", Show(item.Path));
                    writer.WriteString("scope", Scope(item.Scope));
                    writer.WriteString("timing", item.Timing == LoadTiming.AtLaunch ? "at-launch" : "when-read");
                    writer.WriteString("rule", item.Rule.Id);
                    writer.WriteNumber("bytes", item.Bytes);
                    if (item.Cut)
                    {
                        writer.WriteBoolean("cut", true);
                    }

                    WriteImportedFrom(writer, item.Via, Show);
                    if (item.Patterns is { Count: > 0 } patterns)
                    {
                        writer.WriteStartArray("patterns");
                        foreach (var pattern in patterns)
                        {
                            writer.WriteStringValue(pattern);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteStartArray("dropped");
                foreach (var item in harness.Resolution.Dropped)
                {
                    rules.TryAdd(item.Rule.Id, item.Rule);
                    writer.WriteStartObject();
                    writer.WriteString("path", Show(item.Path));
                    writer.WriteString("rule", item.Rule.Id);
                    WriteImportedFrom(writer, item.Via, Show);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("findings");
            foreach (var finding in findings)
            {
                writer.WriteStartObject();
                writer.WriteString("id", finding.Id);
                writer.WriteString("severity", finding.Severity switch
                {
                    Severity.Error => "error",
                    Severity.Warning => "warning",
                    _ => "info",
                });
                writer.WriteString("file", finding.File);
                if (finding.Line is { } line)
                {
                    writer.WriteNumber("line", line);
                }

                writer.WriteString("message", finding.Message);
                writer.WriteString("fix", finding.Fix);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartObject("rules");
            foreach (var rule in rules.Values)
            {
                writer.WriteStartObject(rule.Id);
                writer.WriteString("label", rule.Label);
                writer.WriteString("summary", rule.Summary);
                writer.WriteString("source", rule.Source);
                writer.WriteBoolean("leftToModel", rule.LeftToModel);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private static void WriteImportedFrom(Utf8JsonWriter writer, ImportSite? via, Func<string, string> show)
    {
        if (via is not null)
        {
            writer.WriteString("importedFrom", $"{show(via.File)}:{via.Line}");
        }
    }

    private static string Scope(InstructionScope scope) => scope switch
    {
        InstructionScope.Managed => "managed",
        InstructionScope.User => "user",
        InstructionScope.Project => "project",
        InstructionScope.Local => "local",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null),
    };
}
