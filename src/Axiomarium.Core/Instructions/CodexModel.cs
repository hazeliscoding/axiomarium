namespace Axiomarium.Core.Instructions;

/// <summary>The rules Codex loads AGENTS.md files by.</summary>
public static class CodexRules
{
    private const string Guide = "https://learn.chatgpt.com/docs/agent-configuration/agents-md";
    private const string Code = "https://github.com/openai/codex/blob/main/codex-rs/core/src/agents_md.rs";

    /// <summary>The global file in CODEX_HOME.</summary>
    public static HarnessRule Global { get; } = new(
        "codex/global", "global", "The global AGENTS.md in CODEX_HOME loads first, and doesn't count toward the byte budget.", Guide);

    /// <summary>A global override replaces the global AGENTS.md.</summary>
    public static HarnessRule GlobalOverride { get; } = new(
        "codex/global-override", "replaced by AGENTS.override.md", "An AGENTS.override.md in CODEX_HOME that isn't empty replaces the global AGENTS.md.", Guide);

    /// <summary>One file from each directory of the project chain.</summary>
    public static HarnessRule ProjectChain { get; } = new(
        "codex/project-chain", "project", "One file loads from each directory, from the project root down to the launch directory.", Guide);

    /// <summary>Only a directory's first file loads.</summary>
    public static HarnessRule OnePerDirectory { get; } = new(
        "codex/one-per-directory", "another file in its folder loads instead", "Only the first of AGENTS.override.md, AGENTS.md and the fallback filenames loads from a directory.", Code);

    /// <summary>An empty override hides the AGENTS.md next to it.</summary>
    public static HarnessRule EmptyOverride { get; } = new(
        "codex/empty-override", "hidden by an empty AGENTS.override.md", "An empty AGENTS.override.md is picked before AGENTS.md, then contributes nothing, so the AGENTS.md next to it never loads.", Code);

    /// <summary>The project files' shared byte budget.</summary>
    public static HarnessRule ByteBudget { get; } = new(
        "codex/byte-budget", "past project_doc_max_bytes", "Project files share project_doc_max_bytes (32 KiB by default): the file that crosses it is cut, and later files are dropped.", Code);

    /// <summary>Files below the launch directory.</summary>
    public static HarnessRule BelowLaunch { get; } = new(
        "codex/below-launch", "below the launch directory", "Codex never loads files below the launch directory. It leaves them to the model, which may or may not look.", Guide, LeftToModel: true);

    /// <summary>Untrusted projects.</summary>
    public static HarnessRule Untrusted { get; } = new(
        "codex/untrusted", "the project is untrusted", "In a project whose trust_level is untrusted, Codex skips every project file.", Code);

    /// <summary>Every Codex rule.</summary>
    public static IReadOnlyList<HarnessRule> All { get; } =
        [Global, GlobalOverride, ProjectChain, OnePerDirectory, EmptyOverride, ByteBudget, BelowLaunch, Untrusted];
}

/// <summary>What Codex loads for a file: its global AGENTS.md, then one file per directory of the project chain.</summary>
/// <remarks>
/// Follows the Codex AGENTS.md guide and <c>agents_md.rs</c> as read on 2026-09-28, confirmed against
/// Codex <see cref="ConfirmedWith"/> by the recordings in <c>scenarios/</c>.
/// </remarks>
public static class CodexModel
{
    /// <summary>The Codex version the model was confirmed against.</summary>
    public const string ConfirmedWith = "0.156.1";

    private const string OverrideName = "AGENTS.override.md";
    private const string AgentsName = "AGENTS.md";

    /// <summary>Resolves what Codex loads when launched in <paramref name="launchDirectory"/> and the agent works on <paramref name="targetFile"/>.</summary>
    /// <param name="launchDirectory">Where Codex starts.</param>
    /// <param name="targetFile">The file the agent works on. Files between the launch directory and it are reported as not loaded.</param>
    /// <param name="machine">Where CODEX_HOME is.</param>
    /// <returns>The files Codex loads, in context order, and the ones it drops.</returns>
    public static Resolution Resolve(string launchDirectory, string targetFile, Machine machine)
    {
        var config = CodexConfig.Load(machine.CodexHome);
        var loaded = new List<LoadedInstruction>();
        var dropped = new List<DroppedInstruction>();
        LoadGlobal(machine.CodexHome, loaded, dropped);

        var launch = Path.GetFullPath(launchDirectory);
        var root = ProjectRoot(launch, config.RootMarkers, machine.FileSystemRoot) ?? launch;
        string[] names = [OverrideName, AgentsName, .. config.FallbackFilenames];
        var untrusted = config.IsUntrusted(launch) || config.IsUntrusted(root);
        var remaining = config.MaxBytes;
        foreach (var directory in Chain(root, launch))
        {
            var present = Present(directory, names);
            if (present.Count == 0)
            {
                continue;
            }

            if (untrusted)
            {
                dropped.AddRange(present.Select(path => new DroppedInstruction(path, CodexRules.Untrusted)));
                continue;
            }

            if (remaining <= 0)
            {
                dropped.AddRange(present.Select(path => new DroppedInstruction(path, CodexRules.ByteBudget)));
                continue;
            }

            // Codex picks a directory's file by existence, and only then checks whether it's empty.
            var chosen = present[0];
            var content = File.ReadAllBytes(chosen);
            var empty = string.IsNullOrWhiteSpace(System.Text.Encoding.UTF8.GetString(content));
            var hidden = empty && Path.GetFileName(chosen) == OverrideName ? CodexRules.EmptyOverride : CodexRules.OnePerDirectory;
            dropped.AddRange(present.Skip(1).Select(path => new DroppedInstruction(path, hidden)));
            if (empty)
            {
                continue;
            }

            var bytes = (int)Math.Min(content.Length, remaining);
            loaded.Add(new LoadedInstruction(chosen, InstructionScope.Project, LoadTiming.AtLaunch, CodexRules.ProjectChain, bytes, Cut: bytes < content.Length));
            remaining -= bytes;
        }

        DropBelowLaunch(launch, Path.GetFullPath(targetFile), names, dropped);
        var (skills, notListed, listing) = CodexSkills.Resolve(launch, machine, config);
        var (hooks, configured) = CodexHooks.Resolve(launch, machine, config);
        return new Resolution(loaded, dropped) { Skills = skills, NotListed = notListed, Listing = listing, Hooks = hooks, ConfiguredHooks = configured };
    }

    // The global file is trimmed, and the first one that isn't empty wins.
    private static void LoadGlobal(string codexHome, List<LoadedInstruction> loaded, List<DroppedInstruction> dropped)
    {
        var overridePath = Path.Combine(codexHome, OverrideName);
        var agentsPath = Path.Combine(codexHome, AgentsName);
        foreach (var path in new[] { overridePath, agentsPath }.Where(File.Exists))
        {
            var text = File.ReadAllText(path).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            loaded.Add(new LoadedInstruction(path, InstructionScope.User, LoadTiming.AtLaunch, CodexRules.Global, System.Text.Encoding.UTF8.GetByteCount(text)));
            if (path == overridePath && File.Exists(agentsPath))
            {
                dropped.Add(new DroppedInstruction(agentsPath, CodexRules.GlobalOverride));
            }

            return;
        }
    }

    // The nearest directory at or above the launch directory that holds a root marker, such as .git.
    internal static string? ProjectRoot(string launch, IReadOnlyList<string> markers, string fileSystemRoot)
    {
        foreach (var directory in Paths.Upward(launch, fileSystemRoot))
        {
            if (markers.Any(marker => Path.Exists(Path.Combine(directory, marker))))
            {
                return directory;
            }
        }

        return null;
    }

    internal static List<string> Chain(string root, string launch)
    {
        var chain = new List<string>();
        for (var directory = new DirectoryInfo(launch); directory is not null; directory = directory.Parent)
        {
            chain.Add(directory.FullName);
            if (SamePath(directory.FullName, root))
            {
                break;
            }
        }

        chain.Reverse();
        return chain;
    }

    private static void DropBelowLaunch(string launch, string target, string[] names, List<DroppedInstruction> dropped)
    {
        var below = new List<string>();
        for (var directory = new FileInfo(target).Directory; directory is not null && !SamePath(directory.FullName, launch); directory = directory.Parent)
        {
            below.Add(directory.FullName);
        }

        // Only when the target really is below the launch directory.
        if (below.Count == 0 || !SamePath(Path.GetDirectoryName(below[^1]) ?? "", launch))
        {
            return;
        }

        below.Reverse();
        foreach (var directory in below)
        {
            dropped.AddRange(Present(directory, names).Select(path => new DroppedInstruction(path, CodexRules.BelowLaunch)));
        }
    }

    private static List<string> Present(string directory, string[] names) =>
        [.. names.Select(name => Path.Combine(directory, name)).Where(File.Exists)];

    private static bool SamePath(string left, string right) => Paths.Same(left, right);
}
