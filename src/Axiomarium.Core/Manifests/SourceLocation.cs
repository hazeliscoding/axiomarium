namespace Axiomarium.Core.Manifests;

/// <summary>A position in a source file.</summary>
/// <param name="Line">The line, starting at 1.</param>
/// <param name="Column">The column, starting at 1.</param>
public readonly record struct SourceLocation(int Line, int Column);
