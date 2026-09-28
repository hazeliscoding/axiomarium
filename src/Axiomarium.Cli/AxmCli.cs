using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Health;

namespace Axiomarium.Cli;

/// <summary>The <c>axm</c> command line: its commands, options and exit codes.</summary>
public static class AxmCli
{
    /// <summary>Exit code 0: the command ran and found no errors.</summary>
    public const int Passed = 0;

    /// <summary>Exit code 1: the command ran and found at least one error.</summary>
    public const int ErrorsFound = 1;

    /// <summary>Exit code 2: the command couldn't run, whatever the reason.</summary>
    public const int CouldNotRun = 2;

    private static string Version =>
        typeof(AxmCli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>Runs <c>axm</c>.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where reports and help go.</param>
    /// <param name="error">Where problems that stop a command go.</param>
    /// <param name="environment">The process's environment variables, for <c>NO_COLOR</c>, <c>AXM_PLAIN</c> and <c>TERM</c>.</param>
    /// <param name="outputRedirected">Whether <paramref name="output"/> is a file or pipe rather than a terminal.</param>
    /// <param name="errorRedirected">Whether <paramref name="error"/> is a file or pipe rather than a terminal.</param>
    /// <param name="virtualTerminal">Whether the terminal understands ANSI escape codes.</param>
    /// <param name="currentDirectory">The directory commands default to.</param>
    /// <returns>
    /// 0, 1 or 2. See <see cref="Passed"/>, <see cref="ErrorsFound"/> and <see cref="CouldNotRun"/>.
    /// Bad arguments and unexpected failures return 2, never 1.
    /// </returns>
    public static int Run(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?> environment,
        bool outputRedirected,
        bool errorRedirected,
        bool virtualTerminal,
        string currentDirectory)
    {
        var outputStyle = Style.For(outputRedirected, environment, virtualTerminal);
        var errorStyle = Style.For(errorRedirected, environment, virtualTerminal);

        var root = new RootCommand("Build, test, and debug your AI coding environment like software.");
        foreach (var option in root.Options.OfType<VersionOption>())
        {
            option.Action = new VersionAction(output, outputStyle);
        }

        var vaultRoot = new Option<string>("--root")
        {
            Description = "The vault to check. Defaults to the current directory.",
            DefaultValueFactory = _ => currentDirectory,
        };
        var doctor = new Command("doctor", "Check the vault's health: find every asset and validate its manifest.");
        doctor.Options.Add(vaultRoot);
        doctor.SetAction(result => Doctor(Path.GetFullPath(result.GetValue(vaultRoot)!, currentDirectory), output, error, outputStyle, errorStyle));
        root.Subcommands.Add(doctor);

        var parsed = root.Parse(args);

        // Exit code 1 means errors were found, so bad arguments get 2 like anything else that couldn't run.
        if (parsed.Errors.Count > 0)
        {
            WriteCouldNotRun(error, errorStyle, [.. parsed.Errors.Select(parseError => parseError.Message)], "Run axm --help for usage.");
            return CouldNotRun;
        }

        // System.CommandLine's own handler would print a stack trace and exit 1, which means errors found.
        try
        {
            return parsed.Invoke(new InvocationConfiguration { Output = output, Error = error, EnableDefaultExceptionHandler = false });
        }
        catch (Exception problem)
        {
            WriteCouldNotRun(error, errorStyle, [$"the command stopped: {problem.Message}"], hint: null);
            return CouldNotRun;
        }
    }

    private static int Doctor(string vaultRoot, TextWriter output, TextWriter error, Style outputStyle, Style errorStyle)
    {
        var result = Core.Health.Doctor.Run(vaultRoot);
        if (result.Report is not { } report)
        {
            var hint = result.Problem!.Kind == VaultProblemKind.NotAVault ? "Run axm doctor inside a vault, or pass --root <dir>." : null;
            WriteCouldNotRun(error, errorStyle, [result.Problem.Message], hint);
            return CouldNotRun;
        }

        DoctorText.Write(output, report, outputStyle);
        return report.ErrorCount > 0 ? ErrorsFound : Passed;
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

    private sealed class VersionAction(TextWriter output, Style style) : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            new Ink(output, style).Write(Version).Kaomoji(Output.Kaomoji.Version, Palette.Accent).Line();
            return Passed;
        }
    }
}
