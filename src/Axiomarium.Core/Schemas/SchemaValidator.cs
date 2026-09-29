using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Axiomarium.Core.Schemas;

/// <summary>
/// Validates JSON against the subset of JSON Schema 2020-12 that Axiomarium's own schemas use, with
/// messages written for people editing YAML.
/// </summary>
/// <remarks>
/// The subset is <see cref="SupportedKeywords"/>. <c>$ref</c> resolves only local <c>#/$defs/name</c>
/// references, <c>type</c> takes a single type, and <c>additionalProperties</c> is <c>true</c> or <c>false</c>.
/// A schema that uses anything else is outside the contract: the validator throws rather than skip a
/// check it doesn't understand, and a test keeps the repo's schemas inside it.
/// </remarks>
public static class SchemaValidator
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    /// <summary>The keywords this validator understands. Annotations are included, because they are allowed.</summary>
    public static IReadOnlySet<string> SupportedKeywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "$schema", "$id", "title", "description", "$defs", "$ref", "type", "properties", "required",
        "additionalProperties", "minProperties", "items", "minItems", "enum", "pattern", "minLength",
    };

    /// <summary>Validates <paramref name="instance"/> against <paramref name="schema"/>.</summary>
    /// <param name="instance">The value to check. <see langword="null"/> is JSON null.</param>
    /// <param name="schema">The root schema, which holds any <c>$defs</c>.</param>
    /// <param name="path">
    /// Where <paramref name="instance"/> sits in a larger document, such as <c>hook</c> for a manifest's
    /// hook block. Every error's path and message starts from it. Empty for a whole document.
    /// </param>
    /// <returns>
    /// Every error, in order: missing required fields, then each property in the schema's order, then
    /// unknown fields in the instance's order. Empty when the instance is valid.
    /// </returns>
    /// <exception cref="InvalidOperationException">The schema uses a keyword form outside the supported subset.</exception>
    public static IReadOnlyList<SchemaError> Validate(JsonNode? instance, JsonObject schema, string path = "")
    {
        var errors = new List<SchemaError>();
        Check(instance, schema, schema, path, errors);
        return errors;
    }

    private static void Check(JsonNode? instance, JsonObject schema, JsonObject root, string path, List<SchemaError> errors)
    {
        if (schema["$ref"] is JsonValue reference)
        {
            schema = Resolve(reference.GetValue<string>(), root);
        }

        var type = schema["type"];
        if (type is not null && type.GetValueKind() != JsonValueKind.String)
        {
            throw new InvalidOperationException("Unsupported schema: type must name one type, such as \"string\".");
        }

        if (type is not null && !Matches(instance, type.GetValue<string>()))
        {
            var detail = type.GetValue<string>() == "string" && Kind(instance) is "a number" or "a whole number"
                ? [$"Quote it: \"{instance!.ToJsonString()}\""]
                : Array.Empty<string>();
            errors.Add(new SchemaError(path, $"{Subject(path)} must be {Describe(type.GetValue<string>())}, found {Kind(instance)}", detail));
            return;
        }

        if (schema["enum"] is JsonArray allowed)
        {
            var values = allowed.Select(value => value!.GetValue<string>()).ToList();
            if (instance is not JsonValue value || value.GetValueKind() != JsonValueKind.String || !values.Contains(value.GetValue<string>()))
            {
                var hint = $"Allowed: {string.Join(", ", values)}";
                if (instance is null)
                {
                    // An empty YAML value reads as null, which isn't a value the user wrote.
                    errors.Add(new SchemaError(path, $"{Subject(path)} is empty", [hint]));
                    return;
                }

                var shown = instance is JsonValue { } scalar && scalar.GetValueKind() == JsonValueKind.String
                    ? scalar.GetValue<string>()
                    : instance.ToJsonString();
                errors.Add(new SchemaError(path, $"Unknown {Field(path)}: \"{shown}\"", [hint]));
            }

            return;
        }

        switch (instance)
        {
            case JsonObject obj:
                CheckObject(obj, schema, root, path, errors);
                break;
            case JsonArray array:
                if (schema["minItems"] is JsonValue minimum && array.Count < minimum.GetValue<int>())
                {
                    var count = minimum.GetValue<int>();
                    errors.Add(new SchemaError(path, $"{Subject(path)} needs at least {count} {(count == 1 ? "item" : "items")}", []));
                }

                if (schema["items"] is JsonObject items)
                {
                    for (var i = 0; i < array.Count; i++)
                    {
                        Check(array[i], items, root, $"{path}[{i}]", errors);
                    }
                }

                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                CheckString(value.GetValue<string>(), schema, path, errors);
                break;
        }
    }

    private static void CheckObject(JsonObject obj, JsonObject schema, JsonObject root, string path, List<SchemaError> errors)
    {
        if (schema["required"] is JsonArray required)
        {
            foreach (var name in required.Select(field => field!.GetValue<string>()))
            {
                if (!obj.ContainsKey(name))
                {
                    errors.Add(new SchemaError(path, $"Missing required field: {Join(path, name)}", []));
                }
            }
        }

        if (schema["minProperties"] is JsonValue minimum && obj.Count < minimum.GetValue<int>())
        {
            var count = minimum.GetValue<int>();
            errors.Add(new SchemaError(path, $"{Subject(path)} needs at least {count} {(count == 1 ? "entry" : "entries")}", []));
        }

        var properties = schema["properties"] as JsonObject;
        if (properties is not null)
        {
            foreach (var (name, propertySchema) in properties)
            {
                if (obj.TryGetPropertyValue(name, out var value))
                {
                    Check(value, propertySchema!.AsObject(), root, Join(path, name), errors);
                }
            }
        }

        var additional = schema["additionalProperties"];
        if (additional is not null && additional.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException("Unsupported schema: additionalProperties must be true or false.");
        }

        if (additional is not null && additional.GetValueKind() == JsonValueKind.False)
        {
            foreach (var (name, _) in obj)
            {
                if (properties is null || !properties.ContainsKey(name))
                {
                    errors.Add(new SchemaError(Join(path, name), $"Unknown field: {Join(path, name)}", []));
                }
            }
        }
    }

    private static void CheckString(string text, JsonObject schema, string path, List<SchemaError> errors)
    {
        if (schema["minLength"] is JsonValue minLength && text.Length < minLength.GetValue<int>())
        {
            errors.Add(new SchemaError(path, $"{Subject(path)} must not be empty", []));
            return;
        }

        if (schema["pattern"] is JsonValue pattern)
        {
            var regex = Patterns.GetOrAdd(pattern.GetValue<string>(), p => new Regex(p, RegexOptions.CultureInvariant));
            if (!regex.IsMatch(text))
            {
                var detail = schema["description"] is JsonValue description ? [description.GetValue<string>()] : Array.Empty<string>();
                errors.Add(new SchemaError(path, $"{Subject(path)} \"{text}\" has the wrong format", detail));
            }
        }
    }

    private static JsonObject Resolve(string reference, JsonObject root)
    {
        const string prefix = "#/$defs/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal) || root["$defs"]?[reference[prefix.Length..]] is not JsonObject target)
        {
            throw new InvalidOperationException($"Unsupported or unknown $ref: {reference}");
        }

        return target;
    }

    private static bool Matches(JsonNode? instance, string type) => type switch
    {
        "object" => instance is JsonObject,
        "array" => instance is JsonArray,
        "string" => Kind(instance) == "a string",
        "boolean" => Kind(instance) == "a boolean",
        "integer" => Kind(instance) == "a whole number",
        "number" => Kind(instance) is "a number" or "a whole number",
        "null" => instance is null,
        _ => throw new InvalidOperationException($"Unsupported type: {type}"),
    };

    private static string Kind(JsonNode? instance) => instance switch
    {
        null => "null",
        JsonObject => "a mapping",
        JsonArray => "a list",
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => "a string",
            JsonValueKind.True or JsonValueKind.False => "a boolean",
            JsonValueKind.Number => IsWhole(value) ? "a whole number" : "a number",
            _ => "null",
        },
        _ => "null",
    };

    // A number is whole when its source text has no fraction or exponent, so 1.0 stays "a number".
    private static bool IsWhole(JsonValue value) =>
        long.TryParse(value.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static string Describe(string type) => type switch
    {
        "object" => "a mapping",
        "array" => "a list",
        "string" => "a string",
        "boolean" => "a boolean",
        "integer" => "a whole number",
        "number" => "a number",
        _ => "null",
    };

    private static string Subject(string path) => path.Length == 0 ? "The document" : path;

    private static string Field(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";
}
