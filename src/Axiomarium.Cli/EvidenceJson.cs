using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Axiomarium.Core.Evidence;

namespace Axiomarium.Cli;

/// <summary>
/// Writes <c>axm evidence --json</c>: each check's state, the reason for it, its latest run and what changed since. The
/// shape is a contract, versioned by <see cref="SchemaVersion"/>.
/// </summary>
internal static class EvidenceJson
{
    /// <summary>The version of the JSON's shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Writes the statuses.</summary>
    /// <param name="output">Where to write. The JSON is indented and ends with a newline.</param>
    /// <param name="statuses">Each check's status, in the order the repo declares them.</param>
    public static void Write(TextWriter output, IReadOnlyList<EvidenceStatus> statuses)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("command", "evidence");
            writer.WriteStartArray("checks");
            foreach (var status in statuses)
            {
                writer.WriteStartObject();
                writer.WriteString("name", status.Check.Name);
                writer.WriteString("state", status.State.ToString().ToLowerInvariant());
                writer.WriteString("reason", status.Reason);
                if (status.Record is { } record)
                {
                    writer.WriteStartObject("run");
                    writer.WriteString("command", record.Command);
                    writer.WriteBoolean("passed", record.Passed);
                    if (record.Exit is { } exit)
                    {
                        writer.WriteNumber("exit", exit);
                    }
                    else
                    {
                        writer.WriteNull("exit");
                    }

                    writer.WriteString("started", Time(record.Started));
                    writer.WriteString("ended", Time(record.Ended));
                    writer.WriteString("folder", record.Folder);
                    writer.WriteStartObject("recordedBy");
                    writer.WriteString("tool", record.RecordedBy.Tool);
                    writer.WriteString("harness", record.RecordedBy.Harness);
                    writer.WriteString("session", record.RecordedBy.Session);
                    writer.WriteEndObject();
                    writer.WriteString("head", record.Head);
                    writer.WriteEndObject();
                }
                else
                {
                    writer.WriteNull("run");
                }

                Paths(writer, "changed", status.Changed);
                Paths(writer, "added", status.Added);
                Paths(writer, "deleted", status.Deleted);
                writer.WriteBoolean("coversChanged", status.CoversChanged);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        output.Write(Encoding.UTF8.GetString(buffer.WrittenSpan));
        output.Write('\n');
    }

    private static void Paths(Utf8JsonWriter writer, string name, IReadOnlyList<string> paths)
    {
        writer.WriteStartArray(name);
        foreach (var path in paths)
        {
            writer.WriteStringValue(path);
        }

        writer.WriteEndArray();
    }

    private static string Time(DateTimeOffset moment) => moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
