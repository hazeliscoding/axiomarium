using System.Text.RegularExpressions;
using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

/// <summary>Runs <see cref="AxmCli"/> in process and captures what it writes.</summary>
internal static partial class CliRun
{
    /// <summary>
    /// Runs axm as if stdout and stderr were files (plain output), or a terminal, with <paramref name="stdin"/>
    /// as its input. <paramref name="virtualTerminal"/> covers both streams unless <paramref name="errorVirtualTerminal"/> says otherwise.
    /// </summary>
    public static (int ExitCode, string Output, string Error) Run(
        string[] args,
        bool terminal = false,
        Dictionary<string, string?>? environment = null,
        bool virtualTerminal = true,
        string stdin = "",
        bool? errorVirtualTerminal = null,
        Axiomarium.Core.Instructions.Machine? machine = null,
        string? currentDirectory = null)
    {
        // Tests never read the real machine, and these commands read the harnesses' files from it.
        if (machine is null && args is ["doctor" or "explain", ..] or ["hook", "session-doctor", ..])
        {
            throw new InvalidOperationException($"{string.Join(' ', args)} reads the harnesses' files: pass a machine that lives in the test's own folder.");
        }

        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exitCode = AxmCli.Run(
            args,
            new StringReader(stdin),
            output,
            error,
            environment ?? [],
            outputRedirected: !terminal,
            errorRedirected: !terminal,
            outputVirtualTerminal: virtualTerminal,
            errorVirtualTerminal: errorVirtualTerminal ?? virtualTerminal,
            currentDirectory ?? Environment.CurrentDirectory,
            machine);
        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>Removes ANSI color codes.</summary>
    public static string StripColor(string text) => Ansi().Replace(text, "");

    [GeneratedRegex(@"\u001b\[[0-9;]*m")]
    private static partial Regex Ansi();
}
