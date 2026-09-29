using System.Text.Json;

namespace Axiomarium.Core.Triggers;

/// <summary>Reads one line of a harness's JSON event stream, whatever else the line holds.</summary>
internal static class JsonEvents
{
    /// <summary>The line's event, or <see langword="null"/> when the line isn't a JSON object, such as a log line.</summary>
    /// <remarks>
    /// <see cref="JsonDocument"/>, unlike <c>JsonNode</c>, reads an object that has a key twice, which JSON allows
    /// and a real trigger test met in a harness's output.
    /// </remarks>
    public static JsonElement? Parse(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The string property <paramref name="name"/>, or <see langword="null"/> when it's missing or isn't a string.</summary>
    public static string? Text(JsonElement? element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>The property <paramref name="name"/> of an object, or <see langword="null"/>.</summary>
    public static JsonElement? Child(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var child) ? child : null;

    /// <summary>The items of an array, or none when <paramref name="element"/> isn't one.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];
}
