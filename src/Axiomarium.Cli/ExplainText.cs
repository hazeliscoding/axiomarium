using Axiomarium.Cli.Output;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>The report <c>axm explain</c> prints: what each harness loads for a file, and what it drops.</summary>
public static class ExplainText
{
    /// <summary>
    /// Writes the heading, one block per harness with its loaded files then its dropped ones, each finding,
    /// and the summary.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="explanation">What each harness loads.</param>
    /// <param name="findings">The findings in <paramref name="explanation"/>, in the order to show them.</param>
    /// <param name="home">The user's home folder, shown as <c>~</c>.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    public static void Write(TextWriter output, Explanation explanation, IReadOnlyList<InstructionFinding> findings, string home, Style style)
    {
        var ink = new Ink(output, style);
        var show = Shower(explanation, home);
        WriteHeading(ink, show(explanation.Target), diff: false, launch: null);

        var loaded = explanation.Harnesses.SelectMany(harness => harness.Resolution.Loaded).ToList();
        var dropped = explanation.Harnesses.SelectMany(harness => harness.Resolution.Dropped).ToList();
        var columns = Columns.For(loaded, dropped, show);
        foreach (var harness in explanation.Harnesses)
        {
            ink.Write("  ").Write(Title(harness.Harness).ToUpperInvariant(), Palette.Dim)
                .Write(" // ", Palette.Dim).Write($"launched at {Launch(explanation, home)}").Line();
            WriteLoaded(ink, harness.Resolution.Loaded, columns, show);
            foreach (var item in harness.Resolution.Dropped)
            {
                var keyword = item.Rule.LeftToModel ? NotLoaded : Dropped;
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write(show(item.Path).PadRight(columns.Path), Palette.Path)
                    .Write(keyword.PadRight(columns.Keyword), item.Rule.LeftToModel ? Palette.Dim : Palette.Warning)
                    .Write(item.Rule.Label + ImportedBy(item.Via, show))
                    .Line();
            }

            if (harness.Resolution.Loaded.Count == 0 && harness.Resolution.Dropped.Count == 0)
            {
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write("no instruction files", Palette.Dim).Line();
            }

            ink.Line();
            WriteSkills(ink, harness, show);
            WriteHooks(ink, harness, show);
        }

        foreach (var finding in findings)
        {
            FindingText.Write(ink, finding);
        }

        var count = explanation.Harnesses.Count;
        var warnings = findings.Count(finding => finding.Severity == Severity.Warning);
        var infos = findings.Count(finding => finding.Severity == Severity.Info);
        var skills = explanation.Harnesses.Sum(harness => harness.Resolution.Skills.Count);
        var hooks = explanation.Harnesses.Sum(harness => harness.Resolution.Hooks.Count(hook => hook.Runs));
        ink.Write($"{count} harness{(count == 1 ? "" : "es")}")
            .Write(" · ", Palette.Dim).Write($"{loaded.Count} loaded", Palette.Ok)
            .Write(" · ", Palette.Dim).Write($"{dropped.Count} not loaded")
            .Write(" · ", Palette.Dim).Write($"{skills} skill{(skills == 1 ? "" : "s")} listed")
            .Write(" · ", Palette.Dim).Write($"{hooks} hook{(hooks == 1 ? "" : "s")} run{(hooks == 1 ? "s" : "")}");
        if (warnings > 0)
        {
            ink.Write(" · ", Palette.Dim).Write($"{warnings} warning{(warnings == 1 ? "" : "s")}", Palette.Warning);
        }

        if (infos > 0)
        {
            ink.Write(" · ", Palette.Dim).Write($"{infos} info", Palette.Dim);
        }

        ink.Kaomoji(Kaomoji.ForOutcome(0, warnings), warnings > 0 ? Palette.Warning : Palette.Ok).Line();
    }

    /// <summary>
    /// Writes the files only one of the first two harnesses loads, one block per harness, and the summary.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="explanation">What each harness loads. It must hold two harnesses.</param>
    /// <param name="home">The user's home folder, shown as <c>~</c>.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    /// <exception cref="ArgumentException">The explanation doesn't hold two harnesses.</exception>
    public static void WriteDiff(TextWriter output, Explanation explanation, string home, Style style)
    {
        if (explanation.Harnesses is not [var first, var second])
        {
            throw new ArgumentException("A diff compares two harnesses.", nameof(explanation));
        }

        var ink = new Ink(output, style);
        var show = Shower(explanation, home);
        // Without the launch directory, a diff from --cwd would look like one from the repo root.
        WriteHeading(ink, show(explanation.Target), diff: true, explanation.LaunchedAtRepoRoot ? null : Launch(explanation, home));

        var (onlyFirst, onlySecond) = Explainer.Diff(first.Resolution, second.Resolution);
        var (skillsFirst, skillsSecond) = Explainer.DiffSkills(first.Resolution, second.Resolution);
        var columns = Columns.For([.. onlyFirst, .. onlySecond], [], show);
        foreach (var (harness, only, skills) in new[] { (first.Harness, onlyFirst, skillsFirst), (second.Harness, onlySecond, skillsSecond) })
        {
            ink.Write("  ").Write($"ONLY {Title(harness).ToUpperInvariant()}", Palette.Dim).Line();
            WriteLoaded(ink, only, columns, show);
            if (only.Count == 0)
            {
                ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write("no instruction files", Palette.Dim).Line();
            }

            ink.Line();
            if (skills.Count > 0)
            {
                ink.Write("  ").Write($"ONLY {Title(harness).ToUpperInvariant()} SKILLS", Palette.Dim).Line();
                WriteSkillRows(ink, skills, [], show);
                ink.Line();
            }
        }

        ink.Write($"{onlyFirst.Count} file{(onlyFirst.Count == 1 ? "" : "s")} only in {Title(first.Harness)}")
            .Write(" · ", Palette.Dim).Write($"{onlySecond.Count} only in {Title(second.Harness)}")
            .Write(" · ", Palette.Dim).Write($"{skillsFirst.Count} skill{(skillsFirst.Count == 1 ? "" : "s")} only in {Title(first.Harness)}")
            .Write(" · ", Palette.Dim).Write($"{skillsSecond.Count} only in {Title(second.Harness)}")
            .Kaomoji(Kaomoji.AllClear, Palette.Ok)
            .Line();
    }

    /// <summary>The harness's name in prose, such as <c>Claude Code</c>.</summary>
    public static string Title(Harness harness) => harness switch
    {
        Harness.ClaudeCode => "Claude Code",
        Harness.Codex => "Codex",
        _ => throw new ArgumentOutOfRangeException(nameof(harness), harness, null),
    };

    private const string Dropped = "DROPPED";
    private const string NotLoaded = "NOT LOADED";

    private static void WriteHeading(Ink ink, string target, bool diff, string? launch)
    {
        ink.Write("AXM EXPLAIN", Palette.Accent).Write(" // ", Palette.Dim).Write(target, Palette.Path);
        if (diff)
        {
            ink.Write(" // ", Palette.Dim).Write("diff");
        }

        if (launch is not null)
        {
            ink.Write(" // ", Palette.Dim).Write($"launched at {launch}");
        }

        ink.Line();
        ink.Line();
    }

    private static void WriteLoaded(Ink ink, IReadOnlyList<LoadedInstruction> loaded, Columns columns, Func<string, string> show)
    {
        for (var i = 0; i < loaded.Count; i++)
        {
            var item = loaded[i];
            var when = item.Timing == LoadTiming.AtLaunch ? "at launch" : "when the file is read";
            if (item.Cut)
            {
                when += $", cut to {item.Bytes} bytes";
            }

            ink.Write("  ").Write($"{i + 1:00}", Palette.Dim).Write("  ").Write(show(item.Path).PadRight(columns.Path), Palette.Path)
                .Write(LabelOf(item).PadRight(columns.Label), Palette.Bold)
                .Write(when + ImportedBy(item.Via, show), Palette.Dim)
                .Line();
        }
    }

    // The listing's size against its budget, then each listed skill, then those kept out.
    private static void WriteSkills(Ink ink, HarnessResolution harness, Func<string, string> show)
    {
        var resolution = harness.Resolution;
        ink.Write("  ").Write($"{Title(harness.Harness).ToUpperInvariant()} SKILLS", Palette.Dim).Write(" // ", Palette.Dim);
        if (resolution.Skills.Count == 0)
        {
            ink.Write("none listed").Line();
        }
        else
        {
            ink.Write($"{resolution.Skills.Count} listed");
            if (resolution.Listing is { } listing)
            {
                ink.Write(" · ", Palette.Dim)
                    .Write($"{listing.Size:N0} of {listing.Budget:N0} {listing.Unit}", listing.OverBudget ? Palette.Warning : null)
                    .Write(listing.OverBudget ? ", over budget" : "", Palette.Warning)
                    .Write($", assuming {listing.Assumption}", Palette.Dim);
            }

            ink.Line();
        }

        WriteSkillRows(ink, resolution.Skills, resolution.NotListed, show);
        ink.Line();
    }

    // Skills built into the harness take one row between them, since every session lists them.
    private static void WriteSkillRows(Ink ink, IReadOnlyList<AvailableSkill> skills, IReadOnlyList<UnlistedSkill> notListed, Func<string, string> show)
    {
        var rows = new List<(string Name, string Path, string Label, string When)>();
        var builtIns = skills.Where(Explainer.IsBuiltIn).ToList();
        foreach (var skill in skills)
        {
            if (Explainer.IsBuiltIn(skill))
            {
                if (skill == builtIns[0])
                {
                    var kind = skill.Rule == ClaudeCodeSkillRules.BuiltIn ? "built-in" : "bundled";
                    rows.Add(($"{builtIns.Count} {kind} skill{(builtIns.Count == 1 ? "" : "s")}", "", skill.Rule.Label, "at launch"));
                }

                continue;
            }

            var label = skill.Patterns is { Count: > 0 } patterns ? $"paths: {string.Join(", ", patterns)}" : skill.Rule.Label;
            var when = skill.Timing == LoadTiming.AtLaunch ? "at launch" : "when the file is read or edited";
            when += skill.Cut ? ", description cut" : skill.NameOnly ? ", name only" : skill.Fallback is not null ? ", first line as description" : "";
            rows.Add((skill.Name, skill.Path is null ? "built in" : show(skill.Path), label, when));
        }

        var hidden = notListed.Select(skill => (
            skill.Name, Path: skill.Path is null ? "built in" : show(skill.Path), Label: skill.Detail is null ? skill.Rule.Label : $"{skill.Rule.Label}: {skill.Detail}")).ToList();
        var nameWidth = rows.Select(row => row.Name.Length).Concat(hidden.Select(row => row.Name.Length)).DefaultIfEmpty(0).Max() + 3;
        var pathWidth = rows.Select(row => row.Path.Length).Concat(hidden.Select(row => row.Path.Length)).DefaultIfEmpty(0).Max() + 3;
        var labelWidth = rows.Select(row => row.Label.Length).DefaultIfEmpty(0).Max() + 3;
        for (var i = 0; i < rows.Count; i++)
        {
            ink.Write("  ").Write($"{i + 1:00}", Palette.Dim).Write("  ").Write(rows[i].Name.PadRight(nameWidth), Palette.Bold).Write(rows[i].Path.PadRight(pathWidth), Palette.Path)
                .Write(rows[i].Label.PadRight(labelWidth)).Write(rows[i].When, Palette.Dim).Line();
        }

        foreach (var row in hidden)
        {
            ink.Write("  ").Write("--", Palette.Dim).Write("  ").Write(row.Name.PadRight(nameWidth), Palette.Bold).Write(row.Path.PadRight(pathWidth), Palette.Path)
                .Write(NotListed.PadRight(NotListed.Length + 2), Palette.Warning).Write(row.Label).Line();
        }
    }

    // Each hook at each moment: the ones that run are numbered, the others say why not.
    private static void WriteHooks(Ink ink, HarnessResolution harness, Func<string, string> show)
    {
        var hooks = harness.Resolution.Hooks;
        ink.Write("  ").Write($"{Title(harness.Harness).ToUpperInvariant()} HOOKS", Palette.Dim).Write(" // ", Palette.Dim)
            .Write(hooks.Count == 0 ? "none at session start or around an edit" : "at session start and around an edit of the file")
            .Write(harness.Harness == Harness.ClaudeCode && hooks.Count > 0 ? ", in a trusted workspace" : "", Palette.Dim)
            .Line();
        if (hooks.Count == 0)
        {
            ink.Line();
            return;
        }

        var rows = hooks.Select(hook => (
            Moment: hook.Moment switch { HookMoment.SessionStart => "session start", HookMoment.BeforeEdit => "before edit", _ => "after edit" },
            Path: show(hook.Hook.Path),
            Handler: hook.Hook.Handler.Length > 48 ? hook.Hook.Handler[..47] + "…" : hook.Hook.Handler,
            Hook: hook)).ToList();
        var moment = rows.Max(row => row.Moment.Length) + 3;
        var path = rows.Max(row => row.Path.Length) + 3;
        var handler = rows.Max(row => row.Handler.Length) + 3;
        var index = 0;
        foreach (var row in rows)
        {
            var reason = row.Hook.Hook.Condition is not { } condition ? row.Hook.Rule.Label
                : row.Hook.Rule == ClaudeCodeHookRules.IfNoMatch ? $"if {condition} doesn't match"
                : $"{row.Hook.Rule.Label}, if {condition}";
            ink.Write("  ").Write(row.Hook.Runs ? $"{++index:00}" : "--", Palette.Dim).Write("  ").Write(row.Moment.PadRight(moment))
                .Write(row.Path.PadRight(path), Palette.Path).Write(row.Handler.PadRight(handler), Palette.Bold)
                .Write((row.Hook.Runs ? Runs : NotRun).PadRight(NotRun.Length + 2), row.Hook.Runs ? Palette.Ok : Palette.Warning)
                .Write(reason, Palette.Dim).Line();
        }

        ink.Line();
    }

    private const string NotListed = "NOT LISTED";
    private const string Runs = "RUNS";
    private const string NotRun = "NOT RUN";

    private static string LabelOf(LoadedInstruction item) =>
        item.Patterns is { Count: > 0 } patterns ? $"paths: {string.Join(", ", patterns)}" : item.Rule.Label;

    private static string ImportedBy(ImportSite? via, Func<string, string> show) =>
        via is null ? "" : $", imported by {show(via.File)}:{via.Line}";

    private static string Launch(Explanation explanation, string home) =>
        explanation.LaunchedAtRepoRoot ? "the repo root" : DisplayPath.Of(explanation.Launch, explanation.RepoRoot, home);

    private static Func<string, string> Shower(Explanation explanation, string home) => path => DisplayPath.Of(path, explanation.RepoRoot, home);

    // Column widths across the whole report, so every block lines up.
    private sealed record Columns(int Path, int Label, int Keyword)
    {
        public static Columns For(IReadOnlyList<LoadedInstruction> loaded, IReadOnlyList<DroppedInstruction> dropped, Func<string, string> show)
        {
            var paths = loaded.Select(item => item.Path).Concat(dropped.Select(item => item.Path)).Select(path => show(path).Length);
            var keywords = dropped.Select(item => item.Rule.LeftToModel ? NotLoaded.Length : Dropped.Length);
            return new Columns(
                paths.DefaultIfEmpty(0).Max() + 3,
                loaded.Select(item => LabelOf(item).Length).DefaultIfEmpty(0).Max() + 3,
                keywords.DefaultIfEmpty(0).Max() + 2);
        }
    }
}
