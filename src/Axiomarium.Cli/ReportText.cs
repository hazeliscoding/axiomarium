using Axiomarium.Cli.Output;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>The reports <c>axm doctor</c> and <c>axm validate</c> print. Both render the vault from the same <see cref="DoctorReport"/>.</summary>
public static class ReportText
{
    /// <summary>
    /// Writes the doctor's report: the heading, one block per kind with a row per asset, a block with a row
    /// per instruction file and the harnesses that load it, each diagnostic, each finding, and the summary.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">What the doctor found. Without a vault, the asset blocks are left out.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    public static void WriteDoctor(TextWriter output, HealthReport report, Style style)
    {
        var ink = new Ink(output, style);
        ink.Write("AXM DOCTOR", Palette.Accent).Write(" // ", Palette.Dim);
        WriteInventory(ink, report);
        ink.Line();
        ink.Line();

        var index = 0;
        if (report.Vault is { } vault)
        {
            var nameWidth = vault.Assets.Count == 0 ? 0 : vault.Assets.Max(asset => asset.Name.Length) + 3;
            foreach (var group in vault.Assets.GroupBy(asset => asset.Kind))
            {
                ink.Write("  ").Write(group.Key.Folder().ToUpperInvariant(), Palette.Dim).Line();
                foreach (var asset in group)
                {
                    index++;
                    WriteRow(ink, asset, index, nameWidth, vault);
                }

                ink.Line();
            }
        }

        var files = report.Instructions.Files;
        if (files.Count > 0)
        {
            var pathWidth = files.Max(file => file.Path.Length) + 3;
            ink.Write("  ").Write("INSTRUCTIONS", Palette.Dim).Line();
            foreach (var file in files)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(file.Path.PadRight(pathWidth), Palette.Path);
                if (file.LoadedBy.Count == 0)
                {
                    ink.Write("not loaded", Palette.Dim);
                }
                else
                {
                    ink.Write(string.Join(" ", file.LoadedBy.Select(harness => harness.Name())));
                }

                ink.Line();
            }

            ink.Line();
        }

        // The vault's own skills and hooks are assets above; these are what the harnesses find. Skills from
        // outside the repo, which follow the user into every repo, take one row per harness and source.
        var skills = SkillRows(report.Instructions.Skills);
        if (skills.Count > 0)
        {
            var nameWidth = skills.Max(skill => skill.Name.Length) + 3;
            var pathWidth = skills.Max(skill => skill.Path.Length) + 3;
            ink.Write("  ").Write("HARNESS SKILLS", Palette.Dim).Line();
            foreach (var (name, path, status, listed) in skills)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(name.PadRight(nameWidth), Palette.Bold).Write(path.PadRight(pathWidth), Palette.Path)
                    .Write(status, listed ? null : Palette.Dim).Line();
            }

            ink.Line();
        }

        // The repo's hooks one by one; hooks from outside it take one row per file.
        var hooks = HookRows(report.Instructions.Hooks);
        if (hooks.Count > 0)
        {
            var eventWidth = hooks.Max(hook => hook.Event.Length) + 3;
            var pathWidth = hooks.Max(hook => hook.Path.Length) + 3;
            var handlerWidth = hooks.Max(hook => hook.Handler.Length) + 3;
            ink.Write("  ").Write("HARNESS HOOKS", Palette.Dim).Line();
            foreach (var (name, path, handler, harness, blocked) in hooks)
            {
                index++;
                ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(name.PadRight(eventWidth)).Write(path.PadRight(pathWidth), Palette.Path)
                    .Write(handler.PadRight(handlerWidth), Palette.Bold).Write(harness).Write(blocked, Palette.Warning).Line();
            }

            ink.Line();
        }

        foreach (var diagnostic in (report.Vault?.Diagnostics ?? []).Concat(report.Config))
        {
            WriteDiagnostic(ink, diagnostic);
            ink.Line();
        }

        foreach (var finding in report.Instructions.Findings)
        {
            FindingText.Write(ink, finding);
        }

        WriteInventory(ink, report);
        if (report.Instructions.Ignored > 0)
        {
            ink.Write(" · ", Palette.Dim).Write($"{report.Instructions.Ignored} ignored", Palette.Dim);
        }

        WriteOutcome(ink, report.ErrorCount, report.WarningCount, report.InfoCount);
    }

    // What the doctor looked at: the assets, when there is a vault, and the instruction files.
    private static List<(string Name, string Path, string Status, bool Listed)> SkillRows(IReadOnlyList<InventorySkill> skills)
    {
        static string Status(InventorySkill skill) =>
            skill.ListedBy.Count > 0 ? string.Join(" ", skill.ListedBy.Select(harness => harness.Name())) : $"not listed: {skill.NotListed?.Label}";

        var rows = new List<(string Name, string Path, string Status, bool Listed)>();
        foreach (var group in skills.GroupBy(skill => skill.InRepo ? skill.Path : $"{Status(skill)}|{skill.Source?.Label}"))
        {
            var first = group.First();
            var count = group.Count();
            if (first.InRepo || count == 1)
            {
                rows.Add((first.Name, first.Path, Status(first), first.ListedBy.Count > 0));
                continue;
            }

            // The folder every skill in the group sits in.
            var folders = group.Select(skill => skill.Path.Split('/')[..^1]).ToList();
            var shared = folders[0].TakeWhile((segment, at) => folders.All(folder => folder.Length > at && folder[at] == segment)).ToList();
            var label = first.Source?.Label is { } source ? $"{source} skills" : "skills";
            rows.Add(($"{count} {label}", string.Join('/', shared), Status(first), first.ListedBy.Count > 0));
        }

        return rows;
    }

    private static List<(string Event, string Path, string Handler, string Harness, string Blocked)> HookRows(IReadOnlyList<InventoryHook> hooks)
    {
        var rows = new List<(string Event, string Path, string Handler, string Harness, string Blocked)>();
        foreach (var group in hooks.Select((hook, at) => (Hook: hook, At: at)).GroupBy(pair => pair.Hook.InRepo ? $"{pair.At}" : $"{pair.Hook.Harness}|{pair.Hook.Path}", pair => pair.Hook))
        {
            var first = group.First();
            var count = group.Count();
            var blocked = group.Count(hook => hook.Blocked is not null);
            if (first.InRepo || count == 1)
            {
                var handler = first.Handler.Length > 48 ? first.Handler[..47] + "…" : first.Handler;
                rows.Add((first.Event, first.Path, handler, first.Harness.Name(), first.Blocked is { } rule ? $", can't run: {rule.Label}" : ""));
                continue;
            }

            rows.Add(($"{count} hooks", first.Path, "", first.Harness.Name(), blocked > 0 ? $", {blocked} can't run" : ""));
        }

        return rows;
    }

    private static void WriteInventory(Ink ink, HealthReport report)
    {
        if (report.Vault is { } vault)
        {
            ink.Write(Count(vault.Assets.Count, "asset")).Write(" · ", Palette.Dim);
        }

        ink.Write(Count(report.Instructions.Files.Count, "instruction file"));
        if (report.Instructions.Skills.Count > 0)
        {
            ink.Write(" · ", Palette.Dim).Write(Count(report.Instructions.Skills.Count, "skill"));
        }

        if (report.Instructions.Hooks.Count > 0)
        {
            ink.Write(" · ", Palette.Dim).Write(Count(report.Instructions.Hooks.Count, "hook"));
        }
    }

    /// <summary>
    /// Writes the validation report: the heading, each diagnostic, and the summary. It is the doctor's
    /// report without the inventory, so the two never disagree.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="report">What the doctor found.</param>
    /// <param name="style">Whether to add color and a kaomoji. Without either, the text is identical.</param>
    public static void WriteValidate(TextWriter output, DoctorReport report, Style style)
    {
        var ink = new Ink(output, style);
        WriteHeading(ink, "AXM VALIDATE", report);
        WriteProblems(ink, report);
    }

    private static void WriteHeading(Ink ink, string title, DoctorReport report)
    {
        ink.Write(title, Palette.Accent).Write(" // ", Palette.Dim).Write(Count(report.Assets.Count, "asset")).Line();
        ink.Line();
    }

    // Every diagnostic, then the summary line with the outcome's kaomoji.
    private static void WriteProblems(Ink ink, DoctorReport report)
    {
        foreach (var diagnostic in report.Diagnostics)
        {
            WriteDiagnostic(ink, diagnostic);
            ink.Line();
        }

        ink.Write(Count(report.Assets.Count, "asset"));
        WriteOutcome(ink, report.ErrorCount, report.WarningCount, infos: 0);
    }

    // The end of the summary line: errors always, warnings and info when there are any, and the kaomoji.
    private static void WriteOutcome(Ink ink, int errors, int warnings, int infos)
    {
        ink.Write(" · ", Palette.Dim).Write(Count(errors, "error"), errors == 0 ? Palette.Ok : Palette.Error);
        if (warnings > 0)
        {
            ink.Write(" · ", Palette.Dim).Write(Count(warnings, "warning"), Palette.Warning);
        }

        if (infos > 0)
        {
            ink.Write(" · ", Palette.Dim).Write($"{infos} info", Palette.Dim);
        }

        var faceColor = errors > 0 ? Palette.Error : warnings > 0 ? Palette.Warning : Palette.Ok;
        ink.Kaomoji(Kaomoji.ForOutcome(errors, warnings), faceColor).Line();
    }

    private static void WriteRow(Ink ink, DiscoveredAsset asset, int index, int nameWidth, DoctorReport report)
    {
        ink.Write("  ").Write($"{index:00}", Palette.Dim).Write("  ").Write(asset.Name.PadRight(nameWidth), Palette.Bold);

        // An asset without manifest fields always has an error that says why.
        var diagnostics = report.DiagnosticsFor(asset).ToList();
        if (asset.Manifest is not { } manifest || diagnostics.Any(diagnostic => diagnostic.Severity == Severity.Error))
        {
            ink.Write("ERROR", Palette.Error).Line();
            return;
        }

        ink.Write(manifest.Maturity.PadRight(14), Palette.Maturity(manifest.Maturity)).Write(manifest.Version.PadRight(8), Palette.Dim);
        if (diagnostics.Count > 0)
        {
            ink.Write("WARNING", Palette.Warning).Line();
        }
        else
        {
            ink.Write("OK", Palette.Ok).Line();
        }
    }

    // Continuation lines line up under the file, whatever the label's width.
    private static void WriteDiagnostic(Ink ink, Diagnostic diagnostic)
    {
        var label = diagnostic.Severity == Severity.Error ? "ERROR" : "WARNING";
        var indent = new string(' ', label.Length + 2);

        ink.Write(label, diagnostic.Severity == Severity.Error ? Palette.Error : Palette.Warning).Write("  ").Write(diagnostic.File, Palette.Path);
        if (diagnostic.Location is { } location)
        {
            ink.Write($":{location.Line}", Palette.Dim);
        }

        ink.Line();
        ink.Write(indent);
        WriteMessage(ink, diagnostic.Message);
        ink.Line();

        foreach (var detail in diagnostic.Detail)
        {
            ink.Write(indent);
            const string allowed = "Allowed: ";
            if (detail.StartsWith(allowed, StringComparison.Ordinal))
            {
                ink.Write(allowed, Palette.Dim).Write(detail[allowed.Length..], Palette.Ok);
            }
            else
            {
                ink.Write(detail);
            }

            ink.Line();
        }
    }

    // The first quoted value in a message is the bad value, so it gets the error color.
    private static void WriteMessage(Ink ink, string message)
    {
        var open = message.IndexOf('"');
        var close = open < 0 ? -1 : message.IndexOf('"', open + 1);
        if (close < 0)
        {
            ink.Write(message);
            return;
        }

        ink.Write(message[..open]).Write(message[open..(close + 1)], Palette.Error).Write(message[(close + 1)..]);
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";
}
