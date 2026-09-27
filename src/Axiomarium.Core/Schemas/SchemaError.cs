namespace Axiomarium.Core.Schemas;

/// <summary>One way an instance breaks its schema.</summary>
/// <param name="Path">
/// Where, in the same format as <see cref="Manifests.YamlParseResult.Locations"/>: <c>""</c> for the
/// document, <c>supports.codex</c>, <c>inputs[0]</c>.
/// </param>
/// <param name="Message">What is wrong, as one line.</param>
/// <param name="Detail">Lines that help fix it, such as the allowed values. Often empty.</param>
public sealed record SchemaError(string Path, string Message, IReadOnlyList<string> Detail);
