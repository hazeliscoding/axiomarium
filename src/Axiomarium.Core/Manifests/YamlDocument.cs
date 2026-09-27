using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Axiomarium.Core.Manifests;

/// <summary>What reading a YAML file produced.</summary>
/// <param name="Root">The document as JSON, or <see langword="null"/> when there is a <paramref name="Problem"/>.</param>
/// <param name="Locations">
/// Where each value starts, by path: <c>""</c> for the root, <c>name</c>, <c>supports.claude-code</c>,
/// <c>inputs[0]</c>. A mapping entry points at its key, a list item at the item.
/// </param>
/// <param name="Problem">Why the file couldn't be read, or <see langword="null"/>.</param>
public sealed record YamlParseResult(JsonNode? Root, IReadOnlyDictionary<string, SourceLocation> Locations, YamlProblem? Problem);

/// <summary>Why a YAML file couldn't be read.</summary>
/// <param name="Message">A sentence that says what is wrong.</param>
/// <param name="Location">Where, when the parser knows.</param>
public sealed record YamlProblem(string Message, SourceLocation? Location);

/// <summary>Reads one YAML document into JSON, remembering where every value came from.</summary>
/// <remarks>
/// Plain scalars are typed with the YAML 1.2 core schema, so <c>yes</c> and <c>on</c> stay strings,
/// and quoted or block scalars are always strings. Uses YamlDotNet's node API, which needs no
/// reflection and works under NativeAOT.
/// </remarks>
public static partial class YamlDocument
{
    private static readonly Dictionary<string, SourceLocation> NoLocations = [];

    /// <summary>Reads <paramref name="text"/>, which may start with a byte order mark and use CRLF line endings.</summary>
    /// <param name="text">The YAML source.</param>
    /// <returns>The document as JSON with its locations, or the problem that stopped it. Never throws for bad YAML.</returns>
    public static YamlParseResult Parse(string text)
    {
        if (text.StartsWith('﻿'))
        {
            text = text[1..];
        }

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException problem)
        {
            return Failed(Describe(problem, text), Location(problem.Start));
        }

        if (stream.Documents.Count == 0)
        {
            return Failed("The file is empty.", null);
        }

        if (stream.Documents.Count > 1)
        {
            return Failed(
                $"The file must hold one YAML document, found {stream.Documents.Count}.",
                Location(stream.Documents[1].RootNode.Start));
        }

        var locations = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        try
        {
            var root = Convert(stream.Documents[0].RootNode, "", Location(stream.Documents[0].RootNode.Start), locations);
            return new YamlParseResult(root, locations, null);
        }
        catch (NonScalarKeyException problem)
        {
            return Failed("Mapping keys must be plain text.", problem.Location);
        }
    }

    private static YamlParseResult Failed(string message, SourceLocation? location) =>
        new(null, NoLocations, new YamlProblem(message, location));

    private static JsonNode? Convert(YamlNode node, string path, SourceLocation location, Dictionary<string, SourceLocation> locations)
    {
        locations[path] = location;
        switch (node)
        {
            case YamlMappingNode mapping:
                var obj = new JsonObject();
                foreach (var (keyNode, valueNode) in mapping.Children)
                {
                    if (keyNode is not YamlScalarNode { Value: { } key })
                    {
                        throw new NonScalarKeyException(Location(keyNode.Start));
                    }

                    var childPath = path.Length == 0 ? key : $"{path}.{key}";
                    obj[key] = Convert(valueNode, childPath, Location(keyNode.Start), locations);
                }

                return obj;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    var item = sequence.Children[i];
                    array.Add(Convert(item, $"{path}[{i}]", Location(item.Start), locations));
                }

                return array;

            case YamlScalarNode scalar:
                return Scalar(scalar);

            default:
                return null;
        }
    }

    private static JsonNode? Scalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? "";
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        // A number that is already valid JSON keeps its source text, so "1.0" isn't reported back as "1".
        if (JsonNumber().IsMatch(value))
        {
            using var number = JsonDocument.Parse(value);
            return JsonValue.Create(number.RootElement.Clone());
        }

        if (Integer().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (value.StartsWith("0x", StringComparison.Ordinal) && Hex().IsMatch(value))
        {
            return JsonValue.Create(System.Convert.ToInt64(value[2..], 16));
        }

        if (value.StartsWith("0o", StringComparison.Ordinal) && Octal().IsMatch(value))
        {
            return JsonValue.Create(System.Convert.ToInt64(value[2..], 8));
        }

        if (Float().IsMatch(value))
        {
            return JsonValue.Create(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        return JsonValue.Create(value);
    }

    private static SourceLocation Location(Mark mark) => new((int)mark.Line, (int)mark.Column);

    // YamlDotNet prefixes messages with "(Line: 1, Col: 1, Idx: 0) - (Line: …): ", which the
    // location already says. Its duplicate-key message doesn't name the key, so read it from the source.
    private static string Describe(YamlException problem, string text)
    {
        var message = MarkPrefix().Replace(problem.Message, "");
        if (message.StartsWith("Duplicate key", StringComparison.Ordinal))
        {
            var lines = text.Split('\n');
            var line = (int)problem.Start.Line - 1;
            if (line >= 0 && line < lines.Length)
            {
                var from = Math.Max(0, (int)problem.Start.Column - 1);
                var rest = lines[line].TrimEnd('\r')[Math.Min(from, lines[line].TrimEnd('\r').Length)..];
                var colon = rest.IndexOf(':');
                var key = (colon >= 0 ? rest[..colon] : rest).Trim().Trim('"', '\'');
                if (key.Length > 0)
                {
                    return $"Duplicate key \"{key}\".";
                }
            }
        }

        return message.EndsWith('.') ? message : message + ".";
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex JsonNumber();

    [GeneratedRegex(@"^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Integer();

    [GeneratedRegex(@"^0x[0-9a-fA-F]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Hex();

    [GeneratedRegex(@"^0o[0-7]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Octal();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Float();

    [GeneratedRegex(@"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\): ", RegexOptions.CultureInvariant)]
    private static partial Regex MarkPrefix();

    private sealed class NonScalarKeyException(SourceLocation location) : Exception
    {
        public SourceLocation Location { get; } = location;
    }
}
