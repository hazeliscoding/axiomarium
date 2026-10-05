using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Core.Evidence;

/// <summary>What recorded a run.</summary>
/// <param name="Tool"><c>axm evidence record</c>, which ran the command itself, or <c>axm hook evidence-freshness</c>, which saw an agent run it.</param>
/// <param name="Harness">The harness the agent ran in, such as <c>claude-code</c>, for the hook; otherwise <see langword="null"/>.</param>
/// <param name="Session">The harness's session id, for the hook; otherwise <see langword="null"/>.</param>
public sealed record EvidenceSource(string Tool, string? Harness, string? Session);

/// <summary>A check's latest run: what ran, how it ended, and what the files it covered held when it ended.</summary>
/// <param name="Check">The check's name.</param>
/// <param name="Command">The command as it ran.</param>
/// <param name="Passed">Whether it passed.</param>
/// <param name="Exit">Its exit code, or <see langword="null"/> when a failure didn't say which.</param>
/// <param name="Started">When it started.</param>
/// <param name="Ended">When it ended.</param>
/// <param name="Folder">The folder it ran in, relative to the repo root with forward slashes, or <c>.</c> for the root.</param>
/// <param name="RecordedBy">What recorded it.</param>
/// <param name="Head">The commit HEAD named when it ended, for the reader, or <see langword="null"/> in a repo with no commit.</param>
/// <param name="Covers">The check's <c>covers</c> globs when it ran, empty for every file git sees.</param>
/// <param name="Files">The hash of each covered file when it ended, by path, as <see cref="EvidenceFiles.Hash"/> gives them.</param>
public sealed record EvidenceRecord(
    string Check,
    string Command,
    bool Passed,
    int? Exit,
    DateTimeOffset Started,
    DateTimeOffset Ended,
    string Folder,
    EvidenceSource RecordedBy,
    string? Head,
    IReadOnlyList<string> Covers,
    IReadOnlyDictionary<string, string> Files);

/// <summary>Where evidence records live, and their JSON, which <c>schemas/evidence.schema.json</c> describes.</summary>
public static class EvidenceRecords
{
    private const string Time = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>Where <paramref name="check"/>'s record lives: <c>.axm/evidence/&lt;check&gt;.json</c> in the repo.</summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <param name="check">The check's name.</param>
    /// <returns>The record's absolute path.</returns>
    public static string PathOf(string repoRoot, string check) => Path.Combine(repoRoot, ".axm", "evidence", check + ".json");

    /// <summary>The version of the record's shape.</summary>
    public const int SchemaVersion = 1;

    /// <summary><paramref name="record"/> as JSON, with times in UTC to the second and files in path order.</summary>
    /// <param name="record">The record.</param>
    /// <returns>Indented JSON that ends with a line break.</returns>
    public static string ToJson(EvidenceRecord record)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("check", record.Check);
            writer.WriteString("command", record.Command);
            writer.WriteBoolean("passed", record.Passed);
            if (record.Exit is { } exit)
            {
                writer.WriteNumber("exit", exit);
            }

            writer.WriteString("started", record.Started.UtcDateTime.ToString(Time, CultureInfo.InvariantCulture));
            writer.WriteString("ended", record.Ended.UtcDateTime.ToString(Time, CultureInfo.InvariantCulture));
            writer.WriteString("folder", record.Folder);
            writer.WriteStartObject("recordedBy");
            writer.WriteString("tool", record.RecordedBy.Tool);
            if (record.RecordedBy.Harness is { } harness)
            {
                writer.WriteString("harness", harness);
            }

            if (record.RecordedBy.Session is { } session)
            {
                writer.WriteString("session", session);
            }

            writer.WriteEndObject();
            if (record.Head is { } head)
            {
                writer.WriteString("head", head);
            }

            writer.WriteStartArray("covers");
            foreach (var pattern in record.Covers)
            {
                writer.WriteStringValue(pattern);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("files");
            foreach (var (path, hash) in record.Files.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", path);
                writer.WriteString("hash", hash);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>Reads <paramref name="check"/>'s record in the repo at <paramref name="repoRoot"/>.</summary>
    /// <param name="repoRoot">The repo's root folder.</param>
    /// <param name="check">The check's name.</param>
    /// <returns>
    /// The record; neither the record nor a problem when the check never ran; or why the file isn't a record, which
    /// leaves the check missing until it runs again.
    /// </returns>
    public static (EvidenceRecord? Record, string? Problem) Read(string repoRoot, string check)
    {
        var path = PathOf(repoRoot, check);
        if (!File.Exists(path))
        {
            return (null, null);
        }

        var shown = $".axm/evidence/{check}.json";
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception problem) when (problem is JsonException or IOException or UnauthorizedAccessException)
        {
            return (null, $"{shown} can't be read: {problem.Message}");
        }

        if (SchemaValidator.Validate(node, SchemaCatalog.Evidence).FirstOrDefault() is { } error)
        {
            return (null, $"{shown} isn't an evidence record: {error.Message}");
        }

        var json = node!.AsObject();
        if (json["schemaVersion"]!.GetValue<int>() is var version and not SchemaVersion)
        {
            return (null, $"{shown} has schemaVersion {version}, and this axm reads {SchemaVersion}.");
        }

        var source = json["recordedBy"]!.AsObject();
        return (new EvidenceRecord(
            json["check"]!.GetValue<string>(),
            json["command"]!.GetValue<string>(),
            json["passed"]!.GetValue<bool>(),
            json["exit"]?.GetValue<int>(),
            Moment(json["started"]!),
            Moment(json["ended"]!),
            json["folder"]!.GetValue<string>(),
            new EvidenceSource(source["tool"]!.GetValue<string>(), source["harness"]?.GetValue<string>(), source["session"]?.GetValue<string>()),
            json["head"]?.GetValue<string>(),
            [.. json["covers"]!.AsArray().Select(pattern => pattern!.GetValue<string>())],
            json["files"]!.AsArray().ToDictionary(file => file!["path"]!.GetValue<string>(), file => file!["hash"]!.GetValue<string>(), StringComparer.Ordinal)), null);
    }

    private static DateTimeOffset Moment(JsonNode node) =>
        DateTimeOffset.ParseExact(node.GetValue<string>(), Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
