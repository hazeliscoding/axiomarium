using System.Runtime.InteropServices;
using System.Text;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>The unscored Codex session that warms a new sealed Codex home's Windows sandbox.</summary>
/// <param name="Elapsed">How long it took.</param>
/// <param name="Tokens">What it used, or <see langword="null"/> when Codex didn't count it.</param>
/// <param name="Problem">Why it didn't finish, or <see langword="null"/>.</param>
internal sealed record EvalWarmup(TimeSpan Elapsed, TokenCount? Tokens, string? Problem);

/// <summary>What an eval run's sessions came to, or why none could run.</summary>
/// <param name="Results">Each session's result, in the order of the sessions asked for.</param>
/// <param name="Warmup">The Codex warm-up, when there was one.</param>
/// <param name="Leftovers">Folders a stopped run had left behind, which this run removed.</param>
/// <param name="Problem">Why no session could run, such as a missing login, or <see langword="null"/>.</param>
/// <param name="Hint">What to do about it, or <see langword="null"/>.</param>
internal sealed record EvalSessionsRun(IReadOnlyList<EvalSessionResult> Results, EvalWarmup? Warmup, IReadOnlyList<string> Leftovers, string? Problem, string? Hint);

/// <summary>
/// Runs an eval run's sessions in a sealed home, four at a time, each in its own copy of its case's repo with the
/// asset installed, then runs each case's checks. The home, its borrowed logins and the copies are deleted afterwards.
/// </summary>
internal static class EvalSessions
{
    /// <summary>How many sessions run at once, across both harnesses.</summary>
    public const int Parallel = 4;

    /// <summary>How many turns a Claude Code session may take.</summary>
    public const int TurnCap = 50;

    /// <summary>How long a session may run before it's stopped.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    // A check's command, such as axm validate, takes seconds.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromMinutes(2);

    private const string WarmupPrompt = "Run the shell command git --version and reply with its output only.";

    /// <summary>Runs <paramref name="sessions"/>.</summary>
    /// <param name="runner">Starts processes and creates the run's folder.</param>
    /// <param name="sessions">The sessions to run.</param>
    /// <param name="vaultRoot">The vault the assets are in.</param>
    /// <param name="machine">Where the user's real harness folders are, which the logins and model choices come from.</param>
    /// <param name="environment">The environment <c>axm</c> runs in, whose Claude Code session variables the sessions don't inherit.</param>
    /// <param name="axm">The path of the running <c>axm</c>, which the sessions and checks run as <c>axm</c>.</param>
    /// <param name="model">The model to ask each harness for, or <see langword="null"/> for the one the user chose.</param>
    /// <param name="clock">Measures each session's wall time.</param>
    /// <param name="platform">The operating system, which decides the sealed home's logins and the Codex warm-up.</param>
    /// <returns>The results, or why none could run.</returns>
    public static async Task<EvalSessionsRun> RunAsync(
        IHarnessRunner runner,
        IReadOnlyList<EvalSessionSpec> sessions,
        string vaultRoot,
        Machine machine,
        IReadOnlyDictionary<string, string?> environment,
        string axm,
        string? model,
        TimeProvider clock,
        OSPlatform platform)
    {
        var leftovers = runner.RemoveLeftovers("evals");
        var folder = runner.CreateFolder("evals");
        var home = Path.Combine(folder, "home");
        var harnesses = sessions.Select(session => session.Harness).Distinct().ToList();
        var sealedHome = SealedHomes.Plan(home, machine, harnesses, platform);
        try
        {
            if (sealedHome.Problem is not null)
            {
                return new EvalSessionsRun([], null, leftovers, sealedHome.Problem, sealedHome.Hint);
            }

            Write(home, sealedHome.Files);
            foreach (var (from, to) in sealedHome.Logins)
            {
                File.Copy(from, Path.Combine(home, to));
            }

            // Custom agents live in the Codex home, which every Codex session shares, so they're written once, first.
            foreach (var session in sessions.Where(session => session.Harness == Harness.Codex).DistinctBy(session => session.Asset.Folder))
            {
                Write(Path.Combine(home, ".codex"), EvalInstall.Plan(session.Asset, Body(vaultRoot, session.Asset), Harness.Codex, axm, null).CodexHome);
            }

            var variables = Variables(sealedHome, environment, axm);
            var warmup = harnesses.Contains(Harness.Codex) && platform == OSPlatform.Windows
                ? await Warm(runner, folder, home, variables, model, clock)
                : null;

            using var gate = new SemaphoreSlim(Parallel);
            var results = await Task.WhenAll(sessions.Select(async (session, index) =>
            {
                await gate.WaitAsync();
                try
                {
                    return await Run(runner, session, Path.Combine(folder, $"copy-{index + 1}"), vaultRoot, home, variables, axm, model, clock);
                }
                catch (Exception problem) when (problem is not OperationCanceledException)
                {
                    // One session's failure is its own problem: the rest of a long run still counts.
                    return new EvalSessionResult(session, null, [], TimeSpan.Zero, $"the session failed: {problem.Message}");
                }
                finally
                {
                    gate.Release();
                }
            }));
            return new EvalSessionsRun(results, warmup, leftovers, null, null);
        }
        finally
        {
            // The logins go first, so a folder that won't delete doesn't keep a credential.
            foreach (var (_, to) in sealedHome.Logins)
            {
                TryDelete(() => File.Delete(Path.Combine(home, to)));
            }

            await Delete(folder);
        }
    }

    /// <summary>
    /// The environment changes each session and check runs with: the sealed home's variables, <c>PATH</c> with this
    /// <c>axm</c>'s folder first, and none of the Claude Code session variables <c>axm</c> itself may run under.
    /// </summary>
    /// <param name="sealedHome">The sealed home.</param>
    /// <param name="environment">The environment <c>axm</c> runs in.</param>
    /// <param name="axm">The path of the running <c>axm</c>.</param>
    /// <returns>The changes: a value sets a variable, and <see langword="null"/> removes it.</returns>
    public static IReadOnlyDictionary<string, string?> Variables(SealedHome sealedHome, IReadOnlyDictionary<string, string?> environment, string axm)
    {
        var variables = new Dictionary<string, string?>();

        // A child would take the parent's effort and its messaging socket (see the M5 spike in ROADMAP.md).
        foreach (var name in environment.Keys.Where(name =>
            name is "CLAUDECODE" or "CLAUDE_EFFORT" or "CLAUDE_PID" || name.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase)))
        {
            variables[name] = null;
        }

        foreach (var (name, value) in sealedHome.Environment)
        {
            variables[name] = value;
        }

        var path = environment.FirstOrDefault(variable => string.Equals(variable.Key, "PATH", StringComparison.OrdinalIgnoreCase));
        variables[path.Key ?? "PATH"] = $"{Path.GetDirectoryName(axm)}{Path.PathSeparator}{path.Value}";
        return variables;
    }

    /// <summary>The arguments a session runs its harness with. The prompt goes on stdin.</summary>
    /// <param name="harness">The harness.</param>
    /// <param name="allow">The case's <c>allow</c> commands.</param>
    /// <param name="model">The model to ask for, or <see langword="null"/> for the sealed home's.</param>
    /// <returns>
    /// For Claude Code, a headless session that accepts edits in the copy and allows only the case's commands, each
    /// through both its Bash and PowerShell tools, with no MCP servers. For Codex, a session in its workspace-write
    /// sandbox without the user's rules, saved so it can spawn custom agents.
    /// </returns>
    public static IReadOnlyList<string> Arguments(Harness harness, IReadOnlyList<string> allow, string? model) => harness == Harness.ClaudeCode
        ?
        [
            "-p", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--permission-mode", "acceptEdits",
            "--strict-mcp-config", "--max-turns", TurnCap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            .. allow.SelectMany(command => new[] { "--allowedTools", $"Bash({command}:*)", "--allowedTools", $"PowerShell({command}:*)" }),
            .. model is null ? Array.Empty<string>() : ["--model", model],
        ]
        : ["exec", "--json", "-s", "workspace-write", "--skip-git-repo-check", "--ignore-rules", .. model is null ? Array.Empty<string>() : ["-m", model], "-"];

    private static async Task<EvalSessionResult> Run(
        IHarnessRunner runner,
        EvalSessionSpec session,
        string copy,
        string vaultRoot,
        string home,
        IReadOnlyDictionary<string, string?> variables,
        string axm,
        string? model,
        TimeProvider clock)
    {
        try
        {
            var problem = await Build(runner, session, copy, vaultRoot, variables, axm);
            if (problem is not null)
            {
                return new EvalSessionResult(session, null, [], TimeSpan.Zero, problem);
            }

            var claude = session.Harness == Harness.ClaudeCode;
            var started = clock.GetTimestamp();
            var output = await runner.RunAsync(new HarnessCall(
                claude ? "claude" : "codex", Arguments(session.Harness, session.Case.Allow, model), session.Case.Prompt, copy, null, Timeout, variables));
            var elapsed = clock.GetElapsedTime(started);
            if (!output.Started)
            {
                return new EvalSessionResult(session, null, [], elapsed, $"couldn't start: {output.Error}");
            }

            var record = claude
                ? ClaudeCodeSessions.Read(output.Lines)
                : CodexSessions.Read(output.Lines, copy, home, Path.Combine(home, ".codex", "sessions"));
            var stopped = output.ExitCode is null ? $"it ran past the {Timeout.TotalMinutes:0}-minute timeout" : record.Stopped;

            var runs = new Dictionary<string, RunOutcome>();
            foreach (var command in session.Case.Checks.Where(check => check.Kind == CheckKind.Run).Select(check => check.Target).Distinct())
            {
                runs[command] = await Check(runner, command, copy, variables);
            }

            return new EvalSessionResult(session, record, EvalChecks.Evaluate(session.Case.Checks, record.Activity, copy, runs), elapsed, stopped);
        }
        finally
        {
            await Delete(copy);
        }
    }

    // The copy is the case's repo, with the asset installed, committed once, so the agent starts from a clean tree
    // as it would in real work. Git runs in the sealed home, so the user's global config and hooks stay out.
    private static async Task<string?> Build(IHarnessRunner runner, EvalSessionSpec session, string copy, string vaultRoot, IReadOnlyDictionary<string, string?> variables, string axm)
    {
        Directory.CreateDirectory(copy);
        var repo = Path.Combine(session.CaseFolder, "repo");
        if (Directory.Exists(repo))
        {
            foreach (var file in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(copy, Path.GetRelativePath(repo, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }

        var settings = Path.Combine(copy, ".claude", "settings.json");
        var installation = EvalInstall.Plan(session.Asset, Body(vaultRoot, session.Asset), session.Harness, axm, File.Exists(settings) ? File.ReadAllText(settings) : null);
        if (installation.Problem is not null)
        {
            return installation.Problem;
        }

        Write(copy, installation.Copy);
        (string Name, string[] Arguments)[] steps =
        [
            ("init", ["init", "-q"]),
            ("add", ["add", "-A"]),
            ("commit", ["-c", "user.name=axm", "-c", "user.email=axm@localhost", "commit", "-q", "--allow-empty", "-m", "The case's repo"]),
        ];
        foreach (var (name, arguments) in steps)
        {
            var git = await runner.RunAsync(new HarnessCall("git", arguments, "", copy, null, CheckTimeout, variables));
            if (!git.Started || git.ExitCode != 0)
            {
                return $"couldn't make the copy a git repo: git {name} {(git.Started ? $"exited {git.ExitCode}: {git.Error.Trim()}" : git.Error)}";
            }
        }

        return null;
    }

    private static async Task<RunOutcome> Check(IHarnessRunner runner, string command, string copy, IReadOnlyDictionary<string, string?> variables)
    {
        // cmd /s /c runs the rest of the line as typed, the way sh -c does.
        var call = OperatingSystem.IsWindows()
            ? new HarnessCall("cmd", ["/d", "/s", "/c", command], "", copy, null, CheckTimeout, variables)
            : new HarnessCall("sh", ["-c", command], "", copy, null, CheckTimeout, variables);
        var output = await runner.RunAsync(call);
        return output.Started
            ? new RunOutcome(output.ExitCode ?? -1, string.Join('\n', output.Lines) + (output.Error.Length > 0 ? "\n" + output.Error : ""))
            : new RunOutcome(-1, output.Error);
    }

    // Codex's first sandboxed command in a new home stalls for about two minutes on Windows (see the M5 spike), so
    // one session pays that before any scored session starts.
    private static async Task<EvalWarmup> Warm(IHarnessRunner runner, string folder, string home, IReadOnlyDictionary<string, string?> variables, string? model, TimeProvider clock)
    {
        var copy = Directory.CreateDirectory(Path.Combine(folder, "warmup")).FullName;
        try
        {
            var started = clock.GetTimestamp();
            var output = await runner.RunAsync(new HarnessCall("codex", Arguments(Harness.Codex, [], model), WarmupPrompt, copy, null, Timeout, variables));
            var elapsed = clock.GetElapsedTime(started);
            if (!output.Started)
            {
                return new EvalWarmup(elapsed, null, $"couldn't start: {output.Error}");
            }

            var record = CodexSessions.Read(output.Lines, copy, home, null);
            return new EvalWarmup(elapsed, record.Tokens, output.ExitCode is null ? $"it ran past the {Timeout.TotalMinutes:0}-minute timeout" : record.Stopped);
        }
        finally
        {
            await Delete(copy);
        }
    }

    private static string Body(string vaultRoot, DiscoveredAsset asset)
    {
        var content = Path.Combine(vaultRoot, asset.Folder, asset.Kind.ContentFile());
        return File.Exists(content) ? File.ReadAllText(content) : "";
    }

    private static void Write(string root, IEnumerable<Axiomarium.Core.Triggers.WorkspaceFile> files)
    {
        foreach (var file in files)
        {
            var target = Path.Combine(root, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    // A harness that was just stopped can hold a file a moment longer, so the delete waits and retries.
    private static async Task Delete(string folder)
    {
        for (var attempt = 1; attempt <= 5 && Directory.Exists(folder); attempt++)
        {
            if (!TryDelete(() => Directory.Delete(folder, recursive: true)))
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }

    private static bool TryDelete(Action delete)
    {
        try
        {
            delete();
            return true;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
