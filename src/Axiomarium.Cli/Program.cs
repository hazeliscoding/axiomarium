using System.Collections;
using System.Text;
using Axiomarium.Cli;
using Axiomarium.Cli.Output;

// Kaomoji, separators and non-ASCII asset text need UTF-8, whatever the console's code page.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

var environment = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
{
    environment[(string)variable.Key] = (string?)variable.Value;
}

// Hook input is UTF-8 JSON, whatever the console's input code page.
using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

return AxmCli.Run(
    args,
    input,
    Console.Out,
    Console.Error,
    environment,
    Console.IsOutputRedirected,
    Console.IsErrorRedirected,
    outputVirtualTerminal: !Console.IsOutputRedirected && WindowsConsole.TryEnableVirtualTerminal(WindowsConsole.Stream.Output),
    errorVirtualTerminal: !Console.IsErrorRedirected && WindowsConsole.TryEnableVirtualTerminal(WindowsConsole.Stream.Error),
    Environment.CurrentDirectory,
    inputRedirected: Console.IsInputRedirected);
