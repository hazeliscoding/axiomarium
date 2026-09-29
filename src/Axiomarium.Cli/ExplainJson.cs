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
                WriteSkills(writer, harness.Resolution, rules, Show);
                WriteHooks(writer, harness.Resolution, rules, Show);
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

    // The skills a harness lists, in listing order, a built-in one with a null path; the ones it keeps out;
    // and the listing's size against its budget.
    private static void WriteSkills(Utf8JsonWriter writer, Resolution resolution, Dictionary<string, HarnessRule> rules, Func<string, string> show)
    {
        writer.WriteStartArray("skills");
        foreach (var skill in resolution.Skills)
        {
            rules.TryAdd(skill.Rule.Id, skill.Rule);
            writer.WriteStartObject();
            writer.WriteString("name", skill.Name);
            WritePath(writer, skill.Path, show);
            writer.WriteString("timing", skill.Timing == LoadTiming.AtLaunch ? "at-launch" : "when-read");
            writer.WriteString("rule", skill.Rule.Id);
            writer.WriteNumber("chars", skill.Chars);
            if (skill.Cut)
            {
                writer.WriteBoolean("cut", true);
            }

            if (skill.NameOnly)
            {
                writer.WriteBoolean("nameOnly", true);
            }

            if (skill.Patterns is { Count: > 0 } patterns)
            {
                writer.WriteStartArray("patterns");
                foreach (var pattern in patterns)
                {
                    writer.WriteStringValue(pattern);
                }

                writer.WriteEndArray();
            }

            if (skill.Fallback is { } fallback)
            {
                writer.WriteString("fallback", fallback);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("notListed");
        foreach (var skill in resolution.NotListed)
        {
            rules.TryAdd(skill.Rule.Id, skill.Rule);
            writer.WriteStartObject();
            writer.WriteString("name", skill.Name);
            WritePath(writer, skill.Path, show);
            writer.WriteString("rule", skill.Rule.Id);
            if (skill.Detail is { } detail)
            {
                writer.WriteString("detail", detail);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        if (resolution.Listing is { } listing)
        {
            rules.TryAdd(listing.Rule.Id, listing.Rule);
            writer.WriteStartObject("listing");
            writer.WriteNumber("size", listing.Size);
            writer.WriteNumber("budget", listing.Budget);
            writer.WriteString("unit", listing.Unit);
            writer.WriteBoolean("overBudget", listing.OverBudget);
            writer.WriteString("assumption", listing.Assumption);
            writer.WriteString("rule", listing.Rule.Id);
            writer.WriteEndObject();
        }
    }

    // Each hook at each moment for the file, running or not, with the rule that decides it.
    private static void WriteHooks(Utf8JsonWriter writer, Resolution resolution, Dictionary<string, HarnessRule> rules, Func<string, string> show)
    {
        writer.WriteStartArray("hooks");
        foreach (var item in resolution.Hooks)
        {
            rules.TryAdd(item.Rule.Id, item.Rule);
            rules.TryAdd(item.Hook.Source.Id, item.Hook.Source);
            writer.WriteStartObject();
            writer.WriteString("moment", item.Moment switch
            {
                HookMoment.SessionStart => "session-start",
                HookMoment.BeforeEdit => "before-edit",
                _ => "after-edit",
            });
            writer.WriteString("path", show(item.Hook.Path));
            writer.WriteString("event", item.Hook.Event);
            if (item.Hook.Matcher is { } matcher)
            {
                writer.WriteString("matcher", matcher);
            }

            writer.WriteString("handler", item.Hook.Handler);
            if (item.Hook.Condition is { } condition)
            {
                writer.WriteString("if", condition);
            }

            writer.WriteString("input", item.Input);
            writer.WriteBoolean("runs", item.Runs);
            writer.WriteString("rule", item.Rule.Id);
            if (item.Hook.Trust is { } trust)
            {
                writer.WriteString("trust", trust);
            }

            if (item.Hook.Hash is { } hash)
            {
                writer.WriteString("hash", hash);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WritePath(Utf8JsonWriter writer, string? path, Func<string, string> show)
    {
        if (path is null)
        {
            writer.WriteNull("path");
        }
        else
        {
            writer.WriteString("path", show(path));
        }
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
