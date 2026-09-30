using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Hooks;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

/// <summary>The <c>axm</c> command line: its commands, options and exit codes.</summary>
public static partial class AxmCli
{
    /// <summary>Exit code 0: the command ran and found no errors.</summary>
    public const int Passed = 0;

    /// <summary>Exit code 1: the command ran and found at least one error.</summary>
    public const int ErrorsFound = 1;

    /// <summary>Exit code 2: the command couldn't run, whatever the reason.</summary>
    public const int CouldNotRun = 2;

    /// <summary>
    /// Exit code 1 for an <c>axm hook</c> command that couldn't run. Claude Code reads exit code 2 from a
    /// hook as "block the action", so a broken hook reports itself without blocking anything.
    /// </summary>
    public const int HookCouldNotRun = 1;

    private static string Version =>
        typeof(AxmCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Runs <c>axm</c>.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="input">Standard input, which hook commands read. Read only by commands that need it.</param>
    /// <param name="output">Where reports and help go.</param>
    /// <param name="error">Where problems that stop a command go.</param>
    /// <param name="environment">
    /// The process's environment variables: <c>NO_COLOR</c>, <c>AXM_PLAIN</c> and <c>TERM</c> for output, and
    /// the home and harness folders <c>axm explain</c> reads unless <paramref name="machine"/> is given.
    /// </param>
    /// <param name="outputRedirected">Whether <paramref name="output"/> is a file or pipe rather than a terminal.</param>
    /// <param name="errorRedirected">Whether <paramref name="error"/> is a file or pipe rather than a terminal.</param>
    /// <param name="outputVirtualTerminal">Whether the terminal behind <paramref name="output"/> understands ANSI escape codes.</param>
    /// <param name="errorVirtualTerminal">Whether the terminal behind <paramref name="error"/> understands ANSI escape codes.</param>
    /// <param name="currentDirectory">The directory commands default to.</param>
    /// <param name="machine">Where the harnesses' user and managed files are, or <see langword="null"/> to take them from <paramref name="environment"/>. Tests pass their own.</param>
    /// <param name="inputRedirected">Whether <paramref name="input"/> is a file or pipe rather than a terminal, where a command that writes can't ask first.</param>
    /// <param name="runner">Runs harness sessions for <c>axm triggers</c>, or <see langword="null"/> for real processes. Tests pass one that replays captured streams.</param>
    /// <param name="clock">The time, for dating what <c>axm triggers generate</c> writes, or <see langword="null"/> for the system clock.</param>
    /// <param name="platform">
    /// The operating system <c>axm eval</c> plans its sealed home for, or <see langword="null"/> for the one it runs on.
    /// Tests pass their own, so no test depends on the machine it runs on.
    /// </param>
    /// <returns>
    /// 0, 1 or 2. See <see cref="Passed"/>, <see cref="ErrorsFound"/> and <see cref="CouldNotRun"/>.
    /// Bad arguments and unexpected failures return 2, never 1, except for <c>axm hook</c> commands,
    /// which return <see cref="HookCouldNotRun"/> and never 2.
    /// </returns>
    public static int Run(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        bool outputRedirected,
        bool errorRedirected,
        bool outputVirtualTerminal,
        bool errorVirtualTerminal,
        string currentDirectory,
        Machine? machine = null,
        bool inputRedirected = true,
        IHarnessRunner? runner = null,
        TimeProvider? clock = null,
        System.Runtime.InteropServices.OSPlatform? platform = null)
    {
        var outputStyle = Style.For(outputRedirected, environment, outputVirtualTerminal);
        var errorStyle = Style.For(errorRedirected, environment, errorVirtualTerminal);

        var root = new RootCommand("Build, test, and debug your AI coding environment like software.");
        foreach (var option in root.Options.OfType<VersionOption>())
        {
            option.Action = new VersionAction(output, outputStyle);
        }

        var session = new Session(
            output, error, outputStyle, errorStyle, currentDirectory, environment, machine,
            input, inputRedirected, runner ?? new ProcessHarnessRunner(), clock ?? TimeProvider.System, platform ?? CurrentPlatform());
        root.Subcommands.Add(DoctorCommand(session));
        root.Subcommands.Add(VaultCommand("validate", "Check every asset and print only the problems, for CI and hooks.", ReportText.WriteValidate, session));
        root.Subcommands.Add(ListCommand(session));
        root.Subcommands.Add(ExplainCommand(session));
        root.Subcommands.Add(TriggersCommand(session));
        root.Subcommands.Add(EvalCommand(session));
        root.Subcommands.Add(ConflictsCommand(session));
        root.Subcommands.Add(HookCommand(input, session));

        var parsed = root.Parse(args);
        var couldNotRun = args is ["hook", ..] ? HookCouldNotRun : CouldNotRun;

        // Exit code 1 means errors were found, so bad arguments get 2 like anything else that couldn't run.
        if (parsed.Errors.Count > 0)
        {
            WriteCouldNotRun(error, errorStyle, [.. parsed.Errors.Select(parseError => parseError.Message)], "Run axm --help for usage.");
            return couldNotRun;
        }

        // System.CommandLine's own handler would print a stack trace and exit 1, which means errors found.
        try
        {
            return parsed.Invoke(new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false });
        }
        catch (Exception problem)
        {
            WriteCouldNotRun(error, errorStyle, [$"the command stopped: {problem.Message}"], hint: null);
            return couldNotRun;
        }
    }

    // Hook commands read the harness's JSON on stdin and print its reply. Their output is always plain,
    // because the harness reads it.
    private static Command HookCommand(TextReader input, Session session)
    {
        var scopeSheriff = new Command("scope-sheriff", "Warn the agent when an edit leaves the task's scope in .axm/scope.");
        scopeSheriff.SetAction(_ => Reply(ScopeSheriff.Run(input.ReadToEnd()), session));

        var sessionDoctor = new Command("session-doctor", "When a session starts, tell the user and the model what axm doctor finds wrong. Silent when all is well.");
        sessionDoctor.SetAction(_ => Reply(SessionDoctor.Run(input.ReadToEnd(), cwd => session.Machine ?? Machine.FromEnvironment(session.Environment, cwd)), session));

        var hook = new Command("hook", "Hooks for Claude Code to run. Each reads the hook's JSON on stdin.");
        hook.Subcommands.Add(scopeSheriff);
        hook.Subcommands.Add(sessionDoctor);
        return hook;
    }

    // A hook prints only what the harness reads, and a problem goes to stderr with exit 1, never 2.
    private static int Reply(HookResult result, Session session)
    {
        if (result.Problem is { } problem)
        {
            WriteCouldNotRun(session.Error, session.ErrorStyle, [problem], hint: null);
            return HookCouldNotRun;
        }

        if (result.Output is { } reply)
        {
            session.Output.WriteLine(reply);
        }

        return Passed;
    }

    private static Command DoctorCommand(Session session)
    {
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The repo root above it is checked, and the vault there or at --root. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var command = new Command("doctor", "Check the repo's health: its instruction files in any repo, and every asset when it holds a vault.");
        command.Options.Add(folder);
        command.SetAction(result =>
        {
            var start = Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory);
            var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
            var examined = Core.Health.Doctor.Examine(start, machine);
            if (examined.Report is not { } report)
            {
                return CouldNotRunWith(session, examined.Problem!.Message, hint: null);
            }

            ReportText.WriteDoctor(session.Output, report, session.OutputStyle);

            // Warnings and info don't fail the doctor; only errors do.
            return report.ErrorCount > 0 ? ErrorsFound : Passed;
        });
        return command;
    }

    // A command that checks the vault at --root and renders the doctor's report its own way.
    private static Command VaultCommand(string name, string description, Action<TextWriter, DoctorReport, Style> render, Session session)
    {
        var vaultRoot = RootOption(session);
        var command = new Command(name, description);
        command.Options.Add(vaultRoot);
        command.SetAction(result =>
        {
            if (Check(name, result.GetValue(vaultRoot)!, session) is not { } report)
            {
                return CouldNotRun;
            }

            render(session.Output, report, session.OutputStyle);
            return report.ErrorCount > 0 ? ErrorsFound : Passed;
        });
        return command;
    }

    private static Command ListCommand(Session session)
    {
        var vaultRoot = RootOption(session);
        var kind = new Option<string?>("--kind") { Description = "Only this kind of asset." };
        kind.AcceptOnlyFromAmong([.. AssetKinds.All.Select(value => value.ManifestName())]);
        var harness = new Option<string?>("--harness") { Description = "Only assets that support this harness, and how well." };
        harness.AcceptOnlyFromAmong([.. Harnesses.All]);

        var command = new Command("list", "List the vault's assets by kind, with maturity, version and harness support.");
        command.Options.Add(vaultRoot);
        command.Options.Add(kind);
        command.Options.Add(harness);
        command.SetAction(result =>
        {
            if (Check("list", result.GetValue(vaultRoot)!, session) is not { } report)
            {
                return CouldNotRun;
            }

            var kindFilter = result.GetValue(kind) is { } name ? AssetKinds.All.Single(value => value.ManifestName() == name) : (AssetKind?)null;
            ListText.Write(session.Output, report, kindFilter, result.GetValue(harness), session.OutputStyle);

            // Listing judges nothing, so invalid assets don't fail it. axm validate does that.
            return Passed;
        });
        return command;
    }

    private static Command ExplainCommand(Session session)
    {
        var target = new Argument<string>("path") { Description = "The file to explain. It needn't exist yet, but its folder must." };
        var harness = new Option<string>("--harness") { Description = "Only this harness.", DefaultValueFactory = _ => "all" };
        harness.AcceptOnlyFromAmong("all", Harness.ClaudeCode.Name(), Harness.Codex.Name());
        var cwd = new Option<string?>("--cwd") { Description = "Where the harness starts. Defaults to the repo root, or the current directory outside a repo." };
        var diff = new Option<bool>("--diff") { Description = "Only the files one harness loads and the other doesn't." };
        var json = new Option<bool>("--json") { Description = "Print JSON. Its shape is a contract, versioned by schemaVersion." };

        var command = new Command("explain", "Show which instruction files each harness loads for a file, and which it drops, each with its rule.");
        command.Arguments.Add(target);
        command.Options.Add(harness);
        command.Options.Add(cwd);
        command.Options.Add(diff);
        command.Options.Add(json);
        command.SetAction(result =>
        {
            Harness[] harnesses = result.GetValue(harness) switch
            {
                "claude-code" => [Harness.ClaudeCode],
                "codex" => [Harness.Codex],
                _ => [Harness.ClaudeCode, Harness.Codex],
            };
            var (asDiff, asJson) = (result.GetValue(diff), result.GetValue(json));
            if (asDiff && asJson)
            {
                return CouldNotRunWith(session, "--diff is for reading, and --json already lists what each harness loads.", "Drop one of them.");
            }

            if (asDiff && harnesses.Length != 2)
            {
                return CouldNotRunWith(session, "--diff compares two harnesses, so it can't take --harness.", "Drop --harness to compare Claude Code and Codex.");
            }

            var path = result.GetValue(target)!;
            var file = Path.GetFullPath(path, session.CurrentDirectory);
            if (Directory.Exists(file))
            {
                return CouldNotRunWith(session, $"{path} is a folder.", "Pass a file in it. The file needn't exist yet.");
            }

            var folders = new[] { Path.GetDirectoryName(file)!, result.GetValue(cwd) is { } launch ? Path.GetFullPath(launch, session.CurrentDirectory) : null };
            if (folders.OfType<string>().FirstOrDefault(folder => !Directory.Exists(folder)) is { } missing)
            {
                return CouldNotRunWith(session, $"The folder {missing} doesn't exist.", hint: null);
            }

            var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, session.CurrentDirectory);
            var explanation = Explainer.Explain(file, result.GetValue(cwd), harnesses, machine, session.CurrentDirectory);
            if (asDiff)
            {
                ExplainText.WriteDiff(session.Output, explanation, machine.Home, session.OutputStyle);
                return Passed;
            }

            var findings = InstructionFindings.For(explanation, machine);
            if (asJson)
            {
                ExplainJson.Write(session.Output, explanation, findings, machine.Home);
            }
            else
            {
                ExplainText.Write(session.Output, explanation, findings, machine.Home, session.OutputStyle);
            }

            // Every instruction finding is a warning or info, so explain passes whenever it ran.
            return Passed;
        });
        return command;
    }

    private static Command TriggersCommand(Session session)
    {
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The harnesses launch at the repo root above it, and the vault there or at --root adds its skills. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var command = new Command("triggers", "Find skills whose descriptions overlap in each harness's listing, and the terms they share.");
        command.Options.Add(folder);
        command.Subcommands.Add(GenerateCommand(session));
        command.Subcommands.Add(TestCommand(session));
        command.Subcommands.Add(ExportCommand(session));
        command.SetAction(result =>
        {
            var start = Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory);
            var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
            var checkedOverlap = TriggerOverlap.Check(start, machine);
            if (checkedOverlap.Report is not { } report)
            {
                return CouldNotRunWith(session, checkedOverlap.Problem!, hint: null);
            }

            TriggersText.Write(session.Output, report, machine.Home, session.OutputStyle);

            // Overlap is shared wording, not a problem found, so triggers passes whenever it ran.
            return Passed;
        });
        return command;
    }

    private static int CouldNotRunWith(Session session, string message, string? hint)
    {
        WriteCouldNotRun(session.Error, session.ErrorStyle, [message], hint);
        return CouldNotRun;
    }

    private static Option<string> RootOption(Session session) => new("--root")
    {
        Description = "The vault to check. Defaults to the current directory.",
        DefaultValueFactory = _ => session.CurrentDirectory,
    };

    // The report, or null after saying on stderr why the vault couldn't be checked.
    private static DoctorReport? Check(string command, string vaultRoot, Session session)
    {
        var result = Core.Health.Doctor.Run(Path.GetFullPath(vaultRoot, session.CurrentDirectory));
        if (result.Report is { } report)
        {
            return report;
        }

        var hint = result.Problem!.Kind == VaultProblemKind.NotAVault ? $"Run axm {command} inside a vault, or pass --root <dir>." : null;
        WriteCouldNotRun(session.Error, session.ErrorStyle, [result.Problem.Message], hint);
        return null;
    }

    // One "axm:" line per message, with a single kaomoji on the first, then an optional hint.
    private static void WriteCouldNotRun(TextWriter error, Style style, IReadOnlyList<string> messages, string? hint)
    {
        var ink = new Ink(error, style);
        for (var i = 0; i < messages.Count; i++)
        {
            ink.Write("axm: ", Palette.Error).Write(messages[i]);
            if (i == 0)
            {
                ink.Kaomoji(Kaomoji.CouldNotRun, Palette.Warning);
            }

            ink.Line();
        }

        if (hint is not null)
        {
            ink.Write("     ").Write(hint, Palette.Dim).Line();
        }
    }

    // What every command writes to, and how, for one run of axm.
    private sealed record Session(
        TextWriter Output, TextWriter Error, Style OutputStyle, Style ErrorStyle, string CurrentDirectory,
        IReadOnlyDictionary<string, string?> Environment, Machine? Machine,
        TextReader Input, bool InputRedirected, IHarnessRunner Runner, TimeProvider Clock, System.Runtime.InteropServices.OSPlatform Platform);

    private static System.Runtime.InteropServices.OSPlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? System.Runtime.InteropServices.OSPlatform.Windows
        : OperatingSystem.IsMacOS() ? System.Runtime.InteropServices.OSPlatform.OSX
        : System.Runtime.InteropServices.OSPlatform.Linux;

    private sealed class VersionAction(TextWriter output, Style style) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            new Ink(output, style).Write(Version).Kaomoji(Output.Kaomoji.Version, Palette.Accent).Line();
            return Passed;
        }
    }
}
