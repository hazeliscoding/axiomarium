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

var terminal = !Console.IsOutputRedirected || !Console.IsErrorRedirected;

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
    virtualTerminal: terminal && WindowsConsole.TryEnableVirtualTerminal(),
    Environment.CurrentDirectory);
