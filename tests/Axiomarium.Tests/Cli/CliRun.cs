using System.Text.RegularExpressions;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

/// <summary>Runs <see cref="AxmCli"/> in process and captures what it writes.</summary>
internal static partial class CliRun
{
    /// <summary>Runs axm as if stdout and stderr were files (plain output), or a terminal.</summary>
    public static (int ExitCode, string Output, string Error) Run(
        string[] args,
        bool terminal = false,
        Dictionary<string, string?>? environment = null,
        bool virtualTerminal = true)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = AxmCli.Run(
            args,
            output,
            error,
            environment ?? [],
            outputRedirected: !terminal,
            errorRedirected: !terminal,
            virtualTerminal,
            Environment.CurrentDirectory);
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Removes ANSI color codes.</summary>
    public static string StripColor(string text) => Ansi().Replace(text, "");

    [GeneratedRegex(@"\u001b\[[0-9;]*m")]
    private static partial Regex Ansi();
}
