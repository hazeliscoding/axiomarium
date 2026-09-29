using System.Text;

namespace Axiomarium.Core.Instructions;

/// <summary>What Claude Code loads for a file: memory files and rules at launch, and more when the agent reads the file.</summary>
/// <remarks>
/// Follows the Claude Code memory docs as read on 2026-09-28, confirmed against Claude Code
/// <see cref="ConfirmedWith"/> by the recordings in <c>scenarios/</c>. Where the docs and the recordings
/// disagree, the model follows the recordings. The managed-policy file, the managed-only setting and the
/// 4 MiB limit can't be recorded, so they follow the docs.
/// </remarks>
public static class ClaudeCodeModel
{
    /// <summary>The Claude Code version the model was confirmed against.</summary>
    public const string ConfirmedWith = "2.1.284";

    /// <summary>Resolves what Claude Code loads when launched in <paramref name="launchDirectory"/> and the agent reads <paramref name="targetFile"/>.</summary>
    /// <param name="launchDirectory">Where Claude Code starts.</param>
    /// <param name="targetFile">The file the agent reads, which loads the files and rules for its directories.</param>
    /// <param name="machine">Where the user and managed files are, and where the walk from the root starts.</param>
    /// <returns>The files Claude Code loads, in context order, and the ones it drops.</returns>
    public static Resolution Resolve(string launchDirectory, string targetFile, Machine machine)
    {
        var launch = Path.GetFullPath(launchDirectory);
        var target = Path.GetFullPath(targetFile);
        var settings = ClaudeSettings.Load(machine, launch);
        var loading = new Loading(launch, machine.Home, settings);
        var ancestors = Paths.Upward(launch, machine.FileSystemRoot).Reverse().ToList();
        var below = Paths.Below(launch, target);
        var userMemory = Path.Combine(machine.ClaudeConfig, "CLAUDE.md");
        var userRules = loading.Rules(Path.Combine(machine.ClaudeConfig, "rules"), launch);

        // Which AGENTS.md files the setting allows: by default, only when no CLAUDE file is above.
        bool HasClaudeFile(string directory) =>
            new[] { "CLAUDE.md", Path.Combine(".claude", "CLAUDE.md"), "CLAUDE.local.md" }
                .Select(name => Path.Combine(directory, name))
                .Any(path => File.Exists(path) && !Paths.Same(path, userMemory));
        var agentsOff = settings.Mode switch
        {
            InstructionFiles.ClaudeMdOrAgentsMd when ancestors.Any(HasClaudeFile) => ClaudeCodeRules.AgentsMdHidden,
            InstructionFiles.ClaudeMd => ClaudeCodeRules.AgentsMdOff,
            InstructionFiles.ManagedOnly => ClaudeCodeRules.ManagedOnly,
            _ => null,
        };
        var managedOnly = settings.Mode == InstructionFiles.ManagedOnly;

        // At launch: managed, user, then each directory from the root down: its CLAUDE files, its rules
        // without paths, CLAUDE.local.md, then its AGENTS files. The recordings show this order.
        loading.Memory(Path.Combine(machine.ClaudeManaged, "CLAUDE.md"), InstructionScope.Managed, LoadTiming.AtLaunch, ClaudeCodeRules.ManagedMemory);
        loading.Memory(userMemory, InstructionScope.User, LoadTiming.AtLaunch, ClaudeCodeRules.UserMemory, managedOnly);
        foreach (var rule in userRules.Where(rule => rule.Always))
        {
            loading.Rule(rule.Path, InstructionScope.User, LoadTiming.AtLaunch, Loads(rule, ClaudeCodeRules.UserRule), managedOnly);
        }

        foreach (var directory in ancestors)
        {
            loading.Memory(Path.Combine(directory, "CLAUDE.md"), InstructionScope.Project, LoadTiming.AtLaunch, ClaudeCodeRules.AncestorMemory, managedOnly);
            loading.Memory(Path.Combine(directory, ".claude", "CLAUDE.md"), InstructionScope.Project, LoadTiming.AtLaunch, ClaudeCodeRules.AncestorMemory, managedOnly);
            foreach (var rule in loading.Rules(Path.Combine(directory, ".claude", "rules"), directory).Where(rule => rule.Always))
            {
                loading.Rule(rule.Path, InstructionScope.Project, LoadTiming.AtLaunch, Loads(rule, ClaudeCodeRules.AncestorRule), managedOnly);
            }

            loading.Memory(Path.Combine(directory, "CLAUDE.local.md"), InstructionScope.Local, LoadTiming.AtLaunch, ClaudeCodeRules.LocalMemory, managedOnly);
            foreach (var name in new[] { "AGENTS.md", Path.Combine(".claude", "AGENTS.md") })
            {
                loading.Agents(Path.Combine(directory, name), LoadTiming.AtLaunch, ClaudeCodeRules.AgentsMd, agentsOff);
            }
        }

        // On read: nested AGENTS.md first (the plugin's hook adds them), then user path rules, each directory
        // below the launch directory, and the path rules of the launch directory and above.
        foreach (var directory in below)
        {
            var hidden = agentsOff ?? (settings.Mode == InstructionFiles.ClaudeMdOrAgentsMd && HasClaudeFile(directory) ? ClaudeCodeRules.AgentsMdHidden : null);
            loading.Agents(Path.Combine(directory, "AGENTS.md"), LoadTiming.OnRead, ClaudeCodeRules.NestedAgentsMd, hidden);
        }

        foreach (var rule in userRules.Where(rule => rule.Scoped))
        {
            loading.PathRule(rule, InstructionScope.User, target);
        }

        foreach (var directory in below)
        {
            loading.Memory(Path.Combine(directory, "CLAUDE.md"), InstructionScope.Project, LoadTiming.OnRead, ClaudeCodeRules.NestedMemory);
            loading.Memory(Path.Combine(directory, ".claude", "CLAUDE.md"), InstructionScope.Project, LoadTiming.OnRead, ClaudeCodeRules.NestedMemory);
            foreach (var rule in loading.Rules(Path.Combine(directory, ".claude", "rules"), directory))
            {
                if (rule.Always)
                {
                    loading.Rule(rule.Path, InstructionScope.Project, LoadTiming.OnRead, Loads(rule, ClaudeCodeRules.NestedRule));
                }
                else if (rule.Scoped)
                {
                    loading.PathRule(rule, InstructionScope.Project, target);
                }
            }

            loading.Memory(Path.Combine(directory, "CLAUDE.local.md"), InstructionScope.Local, LoadTiming.OnRead, ClaudeCodeRules.NestedMemory, managedOnly);
        }

        foreach (var directory in ancestors)
        {
            foreach (var rule in loading.Rules(Path.Combine(directory, ".claude", "rules"), directory).Where(rule => rule.Scoped))
            {
                loading.PathRule(rule, InstructionScope.Project, target);
            }
        }

        var (skills, notListed, listing) = ClaudeCodeSkills.Resolve(launch, target, machine, settings);
        var (hooks, configured) = ClaudeCodeHooks.Resolve(launch, target, machine, settings);
        return loading.Result() with { Skills = skills, NotListed = notListed, Listing = listing, Hooks = hooks, ConfiguredHooks = configured };
    }

    // A rule whose frontmatter doesn't parse loads where a rule without paths would, and says why.
    private static HarnessRule Loads(RuleFile rule, HarnessRule withoutPaths) =>
        rule.Frontmatter.Valid ? withoutPaths : ClaudeCodeRules.InvalidFrontmatter;

    // What has loaded so far. A file loads once, however many rules reach it.
    private sealed class Loading(string launch, string home, ClaudeSettings settings)
    {
        private const int MaxImportHops = 4;
        private const long MaxBytes = 4 * 1024 * 1024;

        private readonly List<LoadedInstruction> _loaded = [];
        private readonly List<DroppedInstruction> _dropped = [];
        private readonly HashSet<string> _seen = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<RuleFile>> _rules = [];

        public void Memory(string path, InstructionScope scope, LoadTiming timing, HarnessRule rule, bool managedOnly = false)
        {
            if (managedOnly)
            {
                Drop(path, ClaudeCodeRules.ManagedOnly);
                return;
            }

            Add(path, scope, timing, rule, text => text, via: null, hops: 0, followImports: true);
        }

        // Claude Code removes a rule's frontmatter before the model sees it.
        public void Rule(string path, InstructionScope scope, LoadTiming timing, HarnessRule rule, bool managedOnly = false)
        {
            if (managedOnly)
            {
                Drop(path, ClaudeCodeRules.ManagedOnly);
                return;
            }

            Add(path, scope, timing, rule, text => Frontmatter.Split(text).Body, via: null, hops: 0, followImports: false);
        }

        // AGENTS.md files follow imports like CLAUDE files, unless the setting turns them off.
        public void Agents(string path, LoadTiming timing, HarnessRule rule, HarnessRule? off)
        {
            if (off is not null)
            {
                Drop(path, off);
                return;
            }

            Add(path, InstructionScope.Project, timing, rule, text => text, via: null, hops: 0, followImports: true);
        }

        public void PathRule(RuleFile rule, InstructionScope scope, string target)
        {
            if (rule.Matches(target))
            {
                Add(rule.Path, scope, LoadTiming.OnRead, ClaudeCodeRules.PathRule, text => Frontmatter.Split(text).Body, via: null, hops: 0, followImports: false, rule.Frontmatter.Paths);
            }
            else
            {
                _dropped.Add(new DroppedInstruction(rule.Path, ClaudeCodeRules.PathRuleNoMatch));
            }
        }

        // A folder's rules, read once.
        public IReadOnlyList<RuleFile> Rules(string folder, string patternBase)
        {
            if (!_rules.TryGetValue(folder, out var rules))
            {
                rules = RuleFile.In(folder, patternBase);
                _rules[folder] = rules;
            }

            return rules;
        }

        public Resolution Result() => new(_loaded, _dropped);

        private void Drop(string path, HarnessRule rule)
        {
            if (File.Exists(path) && !_seen.Contains(Path.GetFullPath(path)))
            {
                _dropped.Add(new DroppedInstruction(path, rule));
            }
        }

        private void Add(
            string path,
            InstructionScope scope,
            LoadTiming timing,
            HarnessRule rule,
            Func<string, string> visible,
            ImportSite? via,
            int hops,
            bool followImports,
            IReadOnlyList<string>? patterns = null)
        {
            if (!File.Exists(path) || _seen.Contains(Path.GetFullPath(path)))
            {
                return;
            }

            if (rule != ClaudeCodeRules.ManagedMemory && settings.IsExcluded(path))
            {
                _dropped.Add(new DroppedInstruction(path, ClaudeCodeRules.Excluded, via));
                return;
            }

            if (new FileInfo(path).Length > MaxBytes)
            {
                _dropped.Add(new DroppedInstruction(path, ClaudeCodeRules.TooLarge, via));
                return;
            }

            _seen.Add(Path.GetFullPath(path));

            // The recordings show launch files trimmed, and files loaded on read passed as they are.
            var text = HtmlComments.Strip(visible(File.ReadAllText(path)));
            var bytes = Encoding.UTF8.GetByteCount(timing == LoadTiming.AtLaunch ? text.Trim() : text);
            _loaded.Add(new LoadedInstruction(path, scope, timing, rule, bytes, Via: via, Patterns: patterns));
            if (followImports)
            {
                FollowImports(path, text, scope, timing, hops);
            }
        }

        private void FollowImports(string file, string text, InstructionScope scope, LoadTiming timing, int hops)
        {
            foreach (var (target, line) in Imports.Find(text, file, home))
            {
                var site = new ImportSite(file, line);
                if (!Imports.Exists(target))
                {
                    _dropped.Add(new DroppedInstruction(target, ClaudeCodeRules.MissingImport, site));
                }
                else if (scope is InstructionScope.Project or InstructionScope.Local && !Paths.IsUnder(target, launch))
                {
                    _dropped.Add(new DroppedInstruction(target, ClaudeCodeRules.ExternalImport, site));
                }
                else if (hops >= MaxImportHops)
                {
                    _dropped.Add(new DroppedInstruction(target, ClaudeCodeRules.ImportTooDeep, site));
                }
                else
                {
                    Add(target, scope, timing, ClaudeCodeRules.Import, content => content, site, hops + 1, followImports: true);
                }
            }
        }
    }
}
