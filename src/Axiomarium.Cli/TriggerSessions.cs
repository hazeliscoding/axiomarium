using System.Text;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

/// <summary>
/// Runs a trigger test's sessions: builds the throwaway copy, runs each prompt on its harness four at a time,
/// stops each session at its first action that isn't loading a skill, and reads which skills it loaded.
/// </summary>
internal static class TriggerSessions
{
    /// <summary>How many sessions run at once, across both harnesses.</summary>
    public const int Parallel = 4;

    // A first action takes seconds. A session that takes this long is stuck, not thinking.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>Runs <paramref name="sessions"/> in a copy of the repo built from <paramref name="plan"/>, then deletes the copy.</summary>
    /// <param name="runner">Starts the harness sessions and creates the copy's folder.</param>
    /// <param name="plan">What the copy holds.</param>
    /// <param name="repoRoot">The repo the plan's files are copied from.</param>
    /// <param name="sessions">The sessions to run.</param>
    /// <param name="model">The model to ask each harness for, or <see langword="null"/> for the one it's set to.</param>
    /// <param name="home">The home folder, which <c>~/</c> in Codex's commands stands for.</param>
    /// <param name="built">Called with the copy's folder once it's built and before any session runs, such as to read what each harness lists there.</param>
    /// <returns>Each session's result, in the order of <paramref name="sessions"/>.</returns>
    public static async Task<IReadOnlyList<SessionResult>> RunAsync(
        IHarnessRunner runner, WorkspacePlan plan, string repoRoot, IReadOnlyList<TriggerSession> sessions, string? model, string home, Action<string>? built = null)
    {
        var folder = runner.CreateFolder("triggers");
        try
        {
            Build(plan, repoRoot, folder);
            built?.Invoke(folder);
            using var gate = new SemaphoreSlim(Parallel);
            return await Task.WhenAll(sessions.Select(async session =>
            {
                await gate.WaitAsync();
                try
                {
                    return await Run(runner, folder, session, model, home);
                }
                catch (Exception problem) when (problem is not OperationCanceledException)
                {
                    // One session's failure is its own problem: the rest of a long run still counts.
                    return new SessionResult(session, [], null, null, $"the session failed: {problem.Message}");
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        finally
        {
            // A harness that was just stopped can hold a file a moment longer, so the delete waits and retries.
            for (var attempt = 1; attempt <= 5 && Directory.Exists(folder); attempt++)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
        }
    }

    // The copy gets an empty .git, so both harnesses treat it as the repo root, as they would the real one.
    private static void Build(WorkspacePlan plan, string repoRoot, string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        foreach (var path in plan.Copy)
        {
            var target = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(repoRoot, path), target);
        }

        foreach (var file in plan.Write)
        {
            var target = Path.Combine(folder, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static async Task<SessionResult> Run(IHarnessRunner runner, string folder, TriggerSession session, string? model, string home)
    {
        var claude = session.Harness == Harness.ClaudeCode;
        string[] arguments = claude
            ? ["-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence", .. model is null ? Array.Empty<string>() : ["--model", model]]
            : ["exec", "--json", "-s", "read-only", "--ephemeral", "--skip-git-repo-check", .. model is null ? Array.Empty<string>() : ["-m", model], "-"];
        Func<string, bool> endsPick = claude ? ClaudeCodeStream.EndsPick : CodexStream.EndsPick;
        var output = await runner.RunAsync(new HarnessCall(claude ? "claude" : "codex", arguments, session.Text, folder, endsPick, Timeout));
        if (!output.Started)
        {
            return new SessionResult(session, [], null, null, $"couldn't start: {output.Error}");
        }

        if (claude)
        {
            var read = ClaudeCodeStream.Read(output.Lines);
            var problem = read.IsError ? read.Result ?? "Claude Code reported an error" : Unfinished(output, endsPick);
            return new SessionResult(session, problem is null ? ClaudeCodeStream.Loads(read) : [], read.Model, read.Version, problem);
        }

        var codex = CodexStream.Read(output.Lines, folder, home);
        var failure = codex.Error ?? Unfinished(output, endsPick);
        return new SessionResult(session, failure is null ? codex.Loads : [], null, null, failure);
    }

    // A session that never reached its first action was stopped by the timeout, or exited on its own.
    private static string? Unfinished(HarnessOutput output, Func<string, bool> endsPick)
    {
        if (output.Lines.Any(endsPick))
        {
            return null;
        }

        var reason = output.Error.Trim().Split('\n').LastOrDefault(line => line.Trim().Length > 0)?.Trim();
        return output.ExitCode is null
            ? $"stopped after {Timeout.TotalMinutes:0} minutes without acting"
            : $"exited with {output.ExitCode} before acting{(reason is null ? "" : $": {reason}")}";
    }
}
