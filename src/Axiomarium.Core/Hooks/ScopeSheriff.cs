namespace Axiomarium.Core.Hooks;

/// <summary>What a hook command prints, or why it couldn't read its input.</summary>
/// <param name="Output">What to print on stdout for the harness, or <see langword="null"/> to stay silent.</param>
/// <param name="Problem">Why the input couldn't be read, or <see langword="null"/>.</param>
public sealed record HookResult(string? Output, string? Problem);

/// <summary>The scope sheriff hook: warns the agent when an edit leaves the task's declared scope.</summary>
/// <remarks>
/// It only warns and asks for a reason; it never blocks, because the edit has already happened. With no
/// <c>.axm/scope</c> above the session's working directory, it stays silent.
/// </remarks>
public static class ScopeSheriff
{
    /// <summary>Checks one edit.</summary>
    /// <param name="input">The hook input Claude Code wrote on stdin.</param>
    /// <returns>
    /// The warning for the harness when the edit is outside the scope; silence for an edit inside it, for
    /// no scope, and for any event or tool other than an edit; or the problem with the input.
    /// </returns>
    public static HookResult Run(string input)
    {
        if (!ClaudeCodeHook.TryReadEdit(input, out var edit, out var problem))
        {
            return new HookResult(null, problem);
        }

        if (edit is null || TaskScope.Find(edit.Cwd) is not { } scope)
        {
            return new HookResult(null, null);
        }

        var file = Path.GetFullPath(edit.FilePath, edit.Cwd);
        var relative = Path.GetRelativePath(scope.Root, file);
        var inside = relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        var shown = inside ? relative.Replace(Path.DirectorySeparatorChar, '/') : file;
        if (inside && scope.Patterns.Any(pattern => pattern.IsMatch(shown)))
        {
            return new HookResult(null, null);
        }

        var patterns = scope.Patterns.Count == 0 ? "no valid patterns" : string.Join(", ", scope.Patterns.Select(pattern => pattern.Pattern));
        var message = string.Join(
            " ",
            [
                $"{shown} is outside this task's scope ({patterns}).",
                "In your reply to the user, say why this edit was needed.",
                $"If the task grew, add the path to {TaskScope.File}.",
                .. scope.Problems,
            ]);
        return new HookResult(ClaudeCodeHook.AdditionalContext(message), null);
    }
}
