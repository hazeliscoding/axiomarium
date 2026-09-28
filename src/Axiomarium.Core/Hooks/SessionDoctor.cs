using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Core.Hooks;

/// <summary>
/// The session doctor hook: runs the doctor's checks when a Claude Code session starts, and when something
/// is wrong, tells the user in one line and the model in full. Silent when all is well.
/// </summary>
/// <remarks>
/// Only errors and warnings speak up. Info findings are waste, not problems, so they never interrupt a
/// session. The hook never blocks: SessionStart can't be blocked, and a notice is all it gives.
/// </remarks>
public static class SessionDoctor
{
    // The user's line names a few problems; the model gets every one.
    private const int NamedForTheUser = 3;

    /// <summary>Checks the repo a session starts in.</summary>
    /// <param name="input">The hook input Claude Code wrote on stdin.</param>
    /// <param name="machineFor">The machine the harnesses run on, for the session's working directory.</param>
    /// <returns>
    /// The notice when the doctor finds an error or a warning; silence for a repo with neither, and for any
    /// event other than SessionStart; or the problem with the input or the working directory.
    /// </returns>
    public static HookResult Run(string input, Func<string, Machine> machineFor)
    {
        if (!ClaudeCodeHook.TryReadSessionStart(input, out var cwd, out var problem))
        {
            return new HookResult(null, problem);
        }

        if (cwd is null)
        {
            return new HookResult(null, null);
        }

        var examined = Doctor.Examine(cwd, machineFor(cwd));
        if (examined.Report is not { } report)
        {
            return new HookResult(null, examined.Problem!.Message);
        }

        var errors = (report.Vault?.Diagnostics ?? []).Concat(report.Config).Where(diagnostic => diagnostic.Severity == Severity.Error).ToList();
        var warnings = report.Instructions.Findings.Where(finding => finding.Severity == Severity.Warning).ToList();
        var vaultWarnings = (report.Vault?.Diagnostics ?? []).Where(diagnostic => diagnostic.Severity == Severity.Warning).ToList();
        if (errors.Count == 0 && warnings.Count == 0 && vaultWarnings.Count == 0)
        {
            return new HookResult(null, null);
        }

        var named = errors.Select(diagnostic => $"an error in {Where(diagnostic)}")
            .Concat(vaultWarnings.Select(diagnostic => $"a warning in {Where(diagnostic)}"))
            .Concat(warnings.Select(finding => $"{finding.Id} in {Where(finding)}"))
            .ToList();
        var listed = string.Join(", ", named.Take(NamedForTheUser));
        var more = named.Count > NamedForTheUser ? $", and {named.Count - NamedForTheUser} more" : "";
        var counts = string.Join(" and ", new[] { Count(errors.Count, "error"), Count(vaultWarnings.Count + warnings.Count, "warning") }.Where(count => count.Length > 0));
        var userMessage = $"axm doctor found {counts} in this repo: {listed}{more}. Run axm doctor to see them.";

        var lines = new List<string> { "axm doctor checked this repo when the session started and found problems:" };
        lines.AddRange(errors.Select(diagnostic => $"- ERROR {Where(diagnostic)}: {diagnostic.Message}"));
        lines.AddRange(vaultWarnings.Select(diagnostic => $"- WARNING {Where(diagnostic)}: {diagnostic.Message}"));
        lines.AddRange(warnings.Select(finding => $"- WARNING {finding.Id}: {finding.Message} Fix: {finding.Fix}"));
        lines.Add("None of this blocks anything. If a file you were meant to read matters to your task, read it yourself. Change instruction files only when the user asks.");
        return new HookResult(ClaudeCodeHook.SessionNotice(userMessage, string.Join("\n", lines)), null);
    }

    private static string Where(Diagnostic diagnostic) => diagnostic.Location is { } location ? $"{diagnostic.File}:{location.Line}" : diagnostic.File;

    private static string Where(InstructionFinding finding) => finding.Line is { } line ? $"{finding.File}:{line}" : finding.File;

    private static string Count(int count, string noun) => count == 0 ? "" : $"{count} {noun}{(count == 1 ? "" : "s")}";
}
