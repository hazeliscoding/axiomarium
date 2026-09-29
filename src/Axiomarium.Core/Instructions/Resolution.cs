namespace Axiomarium.Core.Instructions;

/// <summary>Where the harnesses keep their user-level files. Injected, so tests never read the real machine.</summary>
/// <param name="Home">The user's home folder, for <c>~</c> in paths.</param>
/// <param name="CodexHome">Codex's folder: <c>$CODEX_HOME</c>, or <c>~/.codex</c>.</param>
/// <param name="ClaudeConfig">Claude Code's folder: <c>$CLAUDE_CONFIG_DIR</c>, or <c>~/.claude</c>.</param>
/// <param name="ClaudeManaged">The folder that holds Claude Code's managed-policy <c>CLAUDE.md</c>, which differs by OS.</param>
/// <param name="FileSystemRoot">Where upward walks stop: the real filesystem root, or a test's own folder.</param>
/// <param name="CodexAdmin">Codex's system folder, which holds admin skills and hooks: <c>/etc/codex</c>, or <c>%ProgramData%\OpenAI\Codex</c> on Windows.</param>
public sealed record Machine(string Home, string CodexHome, string ClaudeConfig, string ClaudeManaged, string FileSystemRoot, string CodexAdmin)
{
    /// <summary>The real machine, from the process's environment variables.</summary>
    /// <param name="environment">The environment: <c>HOME</c> or <c>USERPROFILE</c>, <c>CODEX_HOME</c>, <c>CLAUDE_CONFIG_DIR</c> and, on Windows, <c>ProgramData</c>.</param>
    /// <param name="launchDirectory">Where the harness would start, whose drive or root the upward walks end at.</param>
    /// <returns>The machine, with each harness's default folder where its variable isn't set.</returns>
    public static Machine FromEnvironment(IReadOnlyDictionary<string, string?> environment, string launchDirectory)
    {
        string? Variable(string name) => environment.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;
        var home = (OperatingSystem.IsWindows() ? Variable("USERPROFILE") ?? Variable("HOME") : Variable("HOME") ?? Variable("USERPROFILE"))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var platform = OperatingSystem.IsWindows() ? System.Runtime.InteropServices.OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? System.Runtime.InteropServices.OSPlatform.OSX
            : System.Runtime.InteropServices.OSPlatform.Linux;
        var codexAdmin = OperatingSystem.IsWindows()
            ? System.IO.Path.Combine(Variable("ProgramData") ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenAI", "Codex")
            : "/etc/codex";
        return new Machine(
            home,
            Variable("CODEX_HOME") ?? System.IO.Path.Combine(home, ".codex"),
            Variable("CLAUDE_CONFIG_DIR") ?? System.IO.Path.Combine(home, ".claude"),
            ClaudeManagedFolder(platform),
            System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(launchDirectory)) ?? "/",
            codexAdmin);
    }

    /// <summary>Where Claude Code's managed-policy <c>CLAUDE.md</c> and <c>managed-settings.json</c> live on <paramref name="platform"/>.</summary>
    public static string ClaudeManagedFolder(System.Runtime.InteropServices.OSPlatform platform) =>
        platform == System.Runtime.InteropServices.OSPlatform.Windows ? @"C:\Program Files\ClaudeCode"
        : platform == System.Runtime.InteropServices.OSPlatform.OSX ? "/Library/Application Support/ClaudeCode"
        : "/etc/claude-code";
}

/// <summary>Whose instructions a file holds, as the harness labels them.</summary>
public enum InstructionScope
{
    /// <summary>Set by an administrator for every user on the machine.</summary>
    Managed,

    /// <summary>The user's own, for every project: Codex's global file, or Claude Code's user memory.</summary>
    User,

    /// <summary>The project's, shared through the repo.</summary>
    Project,

    /// <summary>The user's, for this project only, such as <c>CLAUDE.local.md</c>.</summary>
    Local,
}

/// <summary>When a harness puts a file into the model's context.</summary>
public enum LoadTiming
{
    /// <summary>When the session starts, from the launch directory.</summary>
    AtLaunch,

    /// <summary>When the agent reads the target file.</summary>
    OnRead,
}

/// <summary>A loading rule of a harness: why a file loads, or why it doesn't.</summary>
/// <param name="Id">Stable and kebab-case after the harness, such as <c>codex/byte-budget</c>.</param>
/// <param name="Label">The rule in a few words, for a column of output, such as <c>past project_doc_max_bytes</c>.</param>
/// <param name="Summary">The rule, in a sentence.</param>
/// <param name="Source">The doc or source code the rule comes from.</param>
/// <param name="LeftToModel">
/// Whether the harness leaves the file to the model, which may or may not read it: "not loaded by the
/// harness" rather than dropped.
/// </param>
public sealed record HarnessRule(string Id, string Label, string Summary, string Source, bool LeftToModel = false);

/// <summary>Where a file is imported from.</summary>
/// <param name="File">The absolute path of the file that holds the import.</param>
/// <param name="Line">The import's line in that file, from 1.</param>
public sealed record ImportSite(string File, int Line);

/// <summary>An instruction file a harness puts into the model's context.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Scope">Whose instructions they are. An imported file takes the scope of the file that imports it.</param>
/// <param name="Timing">When it loads.</param>
/// <param name="Rule">The rule that loads it.</param>
/// <param name="Bytes">How many bytes of it the model sees, which is fewer than the file's when <paramref name="Cut"/>.</param>
/// <param name="Cut">Whether the harness cut it short, such as at a byte budget.</param>
/// <param name="Via">Where it was imported from, when an import loaded it.</param>
/// <param name="Patterns">The patterns that matched the file, when it's a path-scoped rule.</param>
public sealed record LoadedInstruction(
    string Path, InstructionScope Scope, LoadTiming Timing, HarnessRule Rule, int Bytes, bool Cut = false, ImportSite? Via = null, IReadOnlyList<string>? Patterns = null);

/// <summary>An instruction file that doesn't reach the model, including an import whose file doesn't exist.</summary>
/// <param name="Path">The file's absolute path.</param>
/// <param name="Rule">The rule that drops it.</param>
/// <param name="Via">Where it was imported from, when it's an import.</param>
public sealed record DroppedInstruction(string Path, HarnessRule Rule, ImportSite? Via = null);

/// <summary>What a harness loads for a file, what it drops, and the skills it lists for the model.</summary>
/// <param name="Loaded">The files the model sees, in context order.</param>
/// <param name="Dropped">The files that exist but don't reach the model.</param>
public sealed record Resolution(IReadOnlyList<LoadedInstruction> Loaded, IReadOnlyList<DroppedInstruction> Dropped)
{
    /// <summary>The skills the harness lists for the model, in listing order: those at launch, then those that join when the agent reads or edits the file.</summary>
    public IReadOnlyList<AvailableSkill> Skills { get; init; } = [];

    /// <summary>The skills the harness finds but doesn't list for the model, each with the rule that keeps it out.</summary>
    public IReadOnlyList<UnlistedSkill> NotListed { get; init; } = [];

    /// <summary>The listing's size at launch against its budget, or <see langword="null"/> when the harness has no skills model.</summary>
    public SkillListing? Listing { get; init; }

    /// <summary>The hooks that fire at session start and around an edit of the file, in moment order, each running or not.</summary>
    public IReadOnlyList<MomentHook> Hooks { get; init; } = [];

    /// <summary>Every hook the harness has configured, on every event, in source order.</summary>
    public IReadOnlyList<ConfiguredHook> ConfiguredHooks { get; init; } = [];
}
