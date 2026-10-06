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
/// <param name="Environment">
/// Changes to the environment it inherits: a value sets a variable, and <see langword="null"/> removes it. Or
/// <see langword="null"/> to inherit it as it is.
/// </param>
/// <param name="Attached">
/// Whether it shares <c>axm</c>'s own stdin, stdout and stderr, so the user sees its output as it runs, as for a command
/// <c>axm evidence record</c> runs. Its lines and stderr then come back empty, and <paramref name="Input"/> and
/// <paramref name="StopAfter"/> are ignored.
/// </param>
public sealed record HarnessCall(
    string Command,
    IReadOnlyList<string> Arguments,
    string Input,
    string? Folder,
    Func<string, bool>? StopAfter,
    TimeSpan Timeout,
    IReadOnlyDictionary<string, string?>? Environment = null,
    bool Attached = false);

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

    /// <summary>
    /// Creates an empty folder for a run's throwaway copies, such as a trigger test's or an eval run's, and marks it
    /// as this process's. The caller fills it, and deletes it when done.
    /// </summary>
    /// <param name="purpose">What it's for, such as <c>triggers</c> or <c>evals</c>, which names the folder it goes in.</param>
    /// <returns>The folder's absolute path, outside the home folder.</returns>
    string CreateFolder(string purpose);

    /// <summary>
    /// Deletes the folders that runs for <paramref name="purpose"/> left behind when they were stopped before they
    /// could clean up, such as by a closed terminal. A folder whose process is still running is left alone.
    /// </summary>
    /// <param name="purpose">What the folders were for, as given to <see cref="CreateFolder"/>.</param>
    /// <returns>The names of the folders it deleted.</returns>
    IReadOnlyList<string> RemoveLeftovers(string purpose);
}

/// <summary>Runs harness sessions as processes, with the user's own environment and login.</summary>
/// <param name="scratchRoot">
/// Where scratch folders go, one folder per purpose, or <see langword="null"/> for the drive's root on Windows and
/// <c>/tmp</c> elsewhere: outside the home folder, so no project instructions above them load.
/// </param>
public sealed class ProcessHarnessRunner(string? scratchRoot = null) : IHarnessRunner
{
    // Names the process that owns a scratch folder, so a later run can tell a leftover from a run in progress.
    private const string OwnerFile = "axm.pid";

    private readonly string _scratchRoot = scratchRoot
        ?? (OperatingSystem.IsWindows() ? Path.GetPathRoot(Environment.SystemDirectory)! : "/tmp");

    /// <inheritdoc/>
    public string CreateFolder(string purpose)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_scratchRoot, $"axm-{purpose}", NewName())).FullName;
        File.WriteAllText(Path.Combine(folder, OwnerFile), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return folder;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> RemoveLeftovers(string purpose)
    {
        var root = Path.Combine(_scratchRoot, $"axm-{purpose}");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var removed = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            if (Running(Path.Combine(folder, OwnerFile)))
            {
                continue;
            }

            // A folder something still holds a file in is left for the next run to try again.
            if (ScratchFolders.Delete(folder))
            {
                removed.Add(Path.GetFileName(folder));
            }
        }

        return removed;
    }

    // A folder without an owner, or whose owner is gone, is a leftover. A reused process id keeps it one run longer.
    private static bool Running(string ownerFile)
    {
        try
        {
            using var owner = Process.GetProcessById(int.Parse(File.ReadAllText(ownerFile).Trim(), System.Globalization.CultureInfo.InvariantCulture));
            return !owner.HasExited;
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or FormatException or OverflowException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<HarnessOutput> RunAsync(HarnessCall call, CancellationToken cancellation = default)
    {
        if (Resolve(call.Command) is not { } program)
        {
            return new HarnessOutput(false, [], null, $"{call.Command} isn't on PATH.");
        }

        var scratch = call.Folder is null ? Path.Combine(_scratchRoot, "axm-triggers", NewName()) : null;
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
            RedirectStandardInput = !call.Attached,
            RedirectStandardOutput = !call.Attached,
            RedirectStandardError = !call.Attached,
        };
        if (!call.Attached)
        {
            start.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
        }
        foreach (var argument in shim ? ["/d", "/c", program, .. call.Arguments] : call.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in call.Environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start)!;
        if (call.Attached)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(call.Timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token);
                return new HarnessOutput(true, [], process.ExitCode, "");
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                return new HarnessOutput(true, [], null, "");
            }
        }

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
