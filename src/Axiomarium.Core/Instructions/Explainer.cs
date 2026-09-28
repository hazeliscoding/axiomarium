namespace Axiomarium.Core.Instructions;

/// <summary>A harness whose instruction loading is modeled.</summary>
public enum Harness
{
    /// <summary>Claude Code, as <c>claude-code</c>.</summary>
    ClaudeCode,

    /// <summary>Codex, as <c>codex</c>.</summary>
    Codex,
}

/// <summary>The names harnesses go by on the command line and in manifests.</summary>
public static class HarnessNames
{
    /// <summary>The harness's name, such as <c>claude-code</c>.</summary>
    public static string Name(this Harness harness) => harness switch
    {
        Harness.ClaudeCode => "claude-code",
        Harness.Codex => "codex",
        _ => throw new ArgumentOutOfRangeException(nameof(harness), harness, null),
    };
}

/// <summary>What one harness loads for the file.</summary>
/// <param name="Harness">The harness.</param>
/// <param name="ConfirmedWith">The harness version its model was confirmed against.</param>
/// <param name="Resolution">What it loads and drops.</param>
public sealed record HarnessResolution(Harness Harness, string ConfirmedWith, Resolution Resolution);

/// <summary>What each harness loads for one file.</summary>
/// <param name="Target">The file, as an absolute path.</param>
/// <param name="RepoRoot">The repo the file is in, found by its <c>.git</c>, or <see langword="null"/> outside a repo.</param>
/// <param name="Launch">The launch directory the harnesses were modeled from.</param>
/// <param name="Harnesses">Each harness's resolution, in the order asked for.</param>
public sealed record Explanation(string Target, string? RepoRoot, string Launch, IReadOnlyList<HarnessResolution> Harnesses)
{
    /// <summary>Whether the harnesses were modeled from the repo root. Always false outside a repo.</summary>
    public bool LaunchedAtRepoRoot => RepoRoot is not null && Paths.Same(Launch, RepoRoot);
}

/// <summary>Explains what each harness loads for a file, and what it drops.</summary>
public static class Explainer
{
    /// <summary>Resolves <paramref name="target"/> for each of <paramref name="harnesses"/>.</summary>
    /// <param name="target">The file, absolute or relative to <paramref name="currentDirectory"/>. It needn't exist yet.</param>
    /// <param name="launchDirectory">Where the harnesses start, or <see langword="null"/> for the repo root, or the current directory outside a repo.</param>
    /// <param name="harnesses">The harnesses to resolve, in the order to report them.</param>
    /// <param name="machine">Where the harnesses' user and managed files are.</param>
    /// <param name="currentDirectory">What relative paths are relative to.</param>
    /// <returns>Each harness's resolution.</returns>
    public static Explanation Explain(string target, string? launchDirectory, IReadOnlyList<Harness> harnesses, Machine machine, string currentDirectory)
    {
        var file = Path.GetFullPath(target, currentDirectory);
        var repoRoot = Paths.Upward(Path.GetDirectoryName(file)!, machine.FileSystemRoot)
            .FirstOrDefault(directory => Path.Exists(Path.Combine(directory, ".git")));
        var launch = launchDirectory is not null ? Path.GetFullPath(launchDirectory, currentDirectory) : repoRoot ?? currentDirectory;
        var resolutions = harnesses.Select(harness => harness switch
        {
            Harness.ClaudeCode => new HarnessResolution(harness, ClaudeCodeModel.ConfirmedWith, ClaudeCodeModel.Resolve(launch, file, machine)),
            Harness.Codex => new HarnessResolution(harness, CodexModel.ConfirmedWith, CodexModel.Resolve(launch, file, machine)),
            _ => throw new ArgumentOutOfRangeException(nameof(harnesses), harness, null),
        });
        return new Explanation(file, repoRoot, launch, [.. resolutions]);
    }

    /// <summary>The files only one of two resolutions loads, compared by path.</summary>
    /// <returns>What only <paramref name="first"/> loads, and what only <paramref name="second"/> loads, each in its context order.</returns>
    public static (IReadOnlyList<LoadedInstruction> OnlyFirst, IReadOnlyList<LoadedInstruction> OnlySecond) Diff(Resolution first, Resolution second)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var firstPaths = first.Loaded.Select(item => item.Path).ToHashSet(comparer);
        var secondPaths = second.Loaded.Select(item => item.Path).ToHashSet(comparer);
        return ([.. first.Loaded.Where(item => !secondPaths.Contains(item.Path))], [.. second.Loaded.Where(item => !firstPaths.Contains(item.Path))]);
    }
}
