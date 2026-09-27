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

return AxmCli.Run(
    args,
    Console.Out,
    Console.Error,
    environment,
    Console.IsOutputRedirected,
    Console.IsErrorRedirected,
    virtualTerminal: terminal && WindowsConsole.TryEnableVirtualTerminal(),
    Environment.CurrentDirectory);
