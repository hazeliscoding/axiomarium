using System.Diagnostics;
using System.Text;

namespace Axiomarium.Cli;

/// <summary>One harness session to run.</summary>
/// <param name="Command">The harness's command, such as <c>claude</c>, found on <c>PATH</c>.</param>
/// <param name="Arguments">Its arguments. The prompt goes in <paramref name="Input"/>, never here.</param>
/// <param name="Input">What to write to its stdin, which is then closed.</param>
/// <param name="Folder">Where to run it, or <see langword="null"/> for an empty scratch folder outside the home folder, deleted afterwards.</param>
/// <param name="StopAfter">Stops the session after the first line it returns <see langword="true"/> for, or <see langword="null"/> to let it finish.</param>
/// <param name="Timeout">How long the session may run before it's stopped.</param>
public sealed record HarnessCall(string Command, IReadOnlyList<string> Arguments, string Input, string? Folder, Func<string, bool>? StopAfter, TimeSpan Timeout);

/// <summary>What a harness session printed.</summary>
/// <param name="Started">Whether the command was found and started.</param>
/// <param name="Lines">Its stdout, a line each, up to where it was stopped.</param>
/// <param name="ExitCode">Its exit code, or <see langword="null"/> when it was stopped or didn't start.</param>
/// <param name="Error">Its stderr, or why it couldn't start.</param>
public sealed record HarnessOutput(bool Started, IReadOnlyList<string> Lines, int? ExitCode, string Error);

/// <summary>
/// Runs harness sessions for <c>axm triggers</c>, the only way <c>axm</c> reaches a model. Tests replace it with
/// one that replays captured streams, so no test runs a model.
/// </summary>
public interface IHarnessRunner
{
    /// <summary>Runs one session.</summary>
    /// <param name="call">What to run, and where.</param>
    /// <param name="cancellation">Stops the session early.</param>
    /// <returns>What it printed. A command that isn't installed comes back as not started, never as an exception.</returns>
    Task<HarnessOutput> RunAsync(HarnessCall call, CancellationToken cancellation = default);

    /// <summary>Creates an empty folder for a trigger test's throwaway copy. The caller fills it, and deletes it when done.</summary>
    /// <returns>The folder's absolute path, outside the home folder.</returns>
    string CreateFolder();
}

/// <summary>Runs harness sessions as processes, with the user's own environment and login.</summary>
public sealed class ProcessHarnessRunner : IHarnessRunner
{
    /// <summary>Where scratch folders go: outside the home folder, so no project instructions above them load.</summary>
    public static string ScratchRoot { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "axm-triggers")
        : "/tmp/axm-triggers";

    /// <inheritdoc/>
    public string CreateFolder() => Directory.CreateDirectory(Path.Combine(ScratchRoot, NewName())).FullName;

    /// <inheritdoc/>
    public async Task<HarnessOutput> RunAsync(HarnessCall call, CancellationToken cancellation = default)
    {
        if (Resolve(call.Command) is not { } program)
        {
            return new HarnessOutput(false, [], null, $"{call.Command} isn't on PATH.");
        }

        var scratch = call.Folder is null ? Path.Combine(ScratchRoot, NewName()) : null;
        if (scratch is not null)
        {
            Directory.CreateDirectory(scratch);
        }

        try
        {
            return await Run(program, call, call.Folder ?? scratch!, cancellation);
        }
        finally
        {
            if (scratch is not null)
            {
                try
                {
                    Directory.Delete(scratch, recursive: true);
                }
                catch (IOException)
                {
                    // A process that was just stopped can hold the folder a moment longer. It's scratch space.
                }
            }
        }
    }

    private static async Task<HarnessOutput> Run(string program, HarnessCall call, string folder, CancellationToken cancellation)
    {
        // A .cmd shim, such as an npm install makes, only runs through cmd. The prompt stays on stdin, where
        // cmd can't mangle it.
        var shim = OperatingSystem.IsWindows() && Path.GetExtension(program) is ".cmd" or ".bat";
        var start = new ProcessStartInfo(shim ? "cmd.exe" : program)
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in shim ? ["/d", "/c", program, .. call.Arguments] : call.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync(cancellation);
        await process.StandardInput.WriteAsync(call.Input);
        process.StandardInput.Close();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(call.Timeout);
        var lines = new List<string>();
        var stopped = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                lines.Add(line);
                if (call.StopAfter?.Invoke(line) == true)
                {
                    stopped = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }

        if (stopped && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync(CancellationToken.None);
        var stderr = await error.ContinueWith(task => task.IsCompletedSuccessfully ? task.Result : "", TaskScheduler.Default);
        return new HarnessOutput(true, lines, stopped ? null : process.ExitCode, stderr);
    }

    // Sortable by time, and unique across parallel runs.
    private static string NewName() => $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];

    // PATH lookup the way a shell does it: on Windows, with the extensions a command can have.
    private static string? Resolve(string command)
    {
        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat"] : [""];
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return folders
            .SelectMany(folder => extensions.Select(extension => Path.Combine(folder.Trim('"'), command + extension)))
            .FirstOrDefault(File.Exists);
    }
}
