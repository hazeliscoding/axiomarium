using System.CommandLine;

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

    /// <summary>Runs <c>axm</c>.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="output">Where reports and help go.</param>
    /// <param name="error">Where problems that stop a command go.</param>
    /// <param name="environment">The process's environment variables, for <c>NO_COLOR</c>, <c>AXM_PLAIN</c> and <c>TERM</c>.</param>
    /// <param name="outputRedirected">Whether <paramref name="output"/> is a file or pipe rather than a terminal.</param>
    /// <param name="errorRedirected">Whether <paramref name="error"/> is a file or pipe rather than a terminal.</param>
    /// <param name="virtualTerminal">Whether the terminal understands ANSI escape codes.</param>
    /// <param name="currentDirectory">The directory commands default to.</param>
    /// <returns>0, 1 or 2. See <see cref="Passed"/>, <see cref="ErrorsFound"/> and <see cref="CouldNotRun"/>.</returns>
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
        var root = new RootCommand("Build, test, and debug your AI coding environment like software.");

        return root.Parse(args).Invoke(new InvocationConfiguration { Output = output, Error = error });
    }
}
