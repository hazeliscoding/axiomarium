using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

/// <summary>A judge's answer, or why there's none.</summary>
/// <param name="Text">What it answered, or <see langword="null"/> when it couldn't answer.</param>
/// <param name="Model">The model that answered, where the harness names it.</param>
/// <param name="Problem">Why there's no answer, such as a harness that won't start, or <see langword="null"/>.</param>
internal sealed record JudgeAnswer(string? Text, string? Model, string? Problem);

/// <summary>
/// Asks a model one question through a harness, in one turn of text: the judge behind <c>axm conflicts --judge</c> and
/// an eval case's rubric. It runs in an empty folder, with Claude Code's tools and MCP servers off and Codex's
/// sandbox read-only, and nothing is saved.
/// </summary>
internal static class JudgeCalls
{
    /// <summary>How long one answer may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>Asks <paramref name="brief"/>.</summary>
    /// <param name="runner">Starts the harness.</param>
    /// <param name="harness">The harness whose model judges.</param>
    /// <param name="brief">The question, on stdin.</param>
    /// <param name="model">The model to ask for, or <see langword="null"/> for the one the harness is set to.</param>
    /// <param name="environment">Changes to the environment the harness runs with, such as a sealed home, or <see langword="null"/>.</param>
    /// <returns>The answer's text and model, or why there's none: the harness didn't start, reported an error, called a tool or said nothing.</returns>
    public static async Task<JudgeAnswer> AskAsync(IHarnessRunner runner, Harness harness, string brief, string? model, IReadOnlyDictionary<string, string?>? environment = null)
    {
        var claude = harness == Harness.ClaudeCode;
        IReadOnlyList<string> arguments = claude
            ? [.. AxmCli.JudgeArguments, .. model is null ? Array.Empty<string>() : ["--model", model]]
            : ["exec", "--json", "-s", "read-only", "--ephemeral", "--skip-git-repo-check", "--ignore-rules", .. model is null ? Array.Empty<string>() : ["-m", model], "-"];
        var output = await runner.RunAsync(new HarnessCall(claude ? "claude" : "codex", arguments, brief, null, null, Timeout, environment));
        if (!output.Started)
        {
            return new JudgeAnswer(null, null, $"couldn't start {(claude ? "Claude Code" : "Codex")}: {output.Error}");
        }

        if (claude)
        {
            var session = ClaudeCodeStream.Read(output.Lines);
            var text = session.Result ?? string.Concat(session.Steps.Select(step => step.Text));
            return session.IsError || text.Length == 0 ? new JudgeAnswer(null, session.Model, $"Claude Code stopped without an answer{Reason(session.Result, output)}")
                : session.Steps.Any(step => step.Tool is not null) ? new JudgeAnswer(null, session.Model, "the answer called a tool")
                : new JudgeAnswer(text, session.Model, null);
        }

        var codex = CodexSessions.Read(output.Lines, Path.GetTempPath(), Path.GetTempPath(), null);
        return codex.Activity.Reply is { Length: > 0 } reply && codex.Stopped is null
            ? new JudgeAnswer(reply, model, null)
            : new JudgeAnswer(null, model, $"Codex stopped without an answer{Reason(codex.Stopped, output)}");
    }

    private static string Reason(string? stated, HarnessOutput output)
    {
        var reason = stated ?? output.Error.Trim().Split('\n').LastOrDefault(line => line.Trim().Length > 0) ?? (output.ExitCode is null ? "it ran past the timeout" : $"it exited with {output.ExitCode}");
        return $": {reason.Trim()}";
    }
}
