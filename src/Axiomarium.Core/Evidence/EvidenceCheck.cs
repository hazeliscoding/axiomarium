using Axiomarium.Core.Paths;

namespace Axiomarium.Core.Evidence;

/// <summary>A check declared in <c>axiomarium.yaml</c>'s <c>evidence.checks</c>, such as the tests.</summary>
/// <param name="Name">Its kebab-case name, unique in the repo.</param>
/// <param name="Run">The commands that count as running it, each a command's leading words, such as <c>dotnet test</c>.</param>
/// <param name="Covers">Globs for the files it vouches for, relative to the repo root. Empty means every file git sees.</param>
public sealed record EvidenceCheck(string Name, IReadOnlyList<string> Run, IReadOnlyList<Glob> Covers);
