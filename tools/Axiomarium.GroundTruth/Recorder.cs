using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axiomarium.GroundTruth;

/// <summary>Runs the real harnesses on a scenario and writes what they loaded to its expected.json.</summary>
internal static class Recorder
{
    public const string ClaudeCode = "claude-code";
    public const string Codex = "codex";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static void Record(Scenario scenario, IReadOnlyCollection<string> harnesses)
    {
        var run = RunDirectory(scenario.Name);
        try
        {
            CopyDirectory(Path.Combine(scenario.Directory, "repo"), Path.Combine(run, "repo"));
            CopyDirectory(Path.Combine(scenario.Directory, "home"), Path.Combine(run, "home"));

            // A scenario can't hold its own .git, and Codex finds the project root by it.
            Run("git", ["init", "-q"], Path.Combine(run, "repo"), []);

            var path = Path.Combine(scenario.Directory, Scenario.RecordingFile);
            var recording = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject() : [];
            recording["recorded"] = DateTime.Now.ToString("yyyy-MM-dd");
            if (harnesses.Contains(ClaudeCode))
            {
                recording[ClaudeCode] = RecordClaudeCode(scenario, run);
            }

            if (harnesses.Contains(Codex))
            {
                recording[Codex] = RecordCodex(scenario, run);
            }

            File.WriteAllText(path, Ordered(recording).ToJsonString(Indented).Replace("\r\n", "\n") + "\n");
        }
        finally
        {
            DeleteDirectory(run);
        }
    }

    private static JsonObject RecordCodex(Scenario scenario, string run)
    {
        var codexHome = Path.Combine(run, "home", ".codex");
        Directory.CreateDirectory(codexHome);
        var output = Run("codex", ["debug", "prompt-input", "Read the target file."], Launch(scenario, run), new() { ["CODEX_HOME"] = codexHome });

        var files = scenario.MarkdownFiles()
            .Where(pair => pair.Key.StartsWith("repo/", StringComparison.Ordinal) || pair.Key.StartsWith("home/.codex/", StringComparison.Ordinal))
            .ToDictionary();
        return new JsonObject
        {
            ["version"] = Version("codex", last: true),
            ["loaded"] = Entries(CodexBlock.Parse(output, files)),
        };
    }

    private static JsonObject RecordClaudeCode(Scenario scenario, string run)
    {
        // Claude Code reads the fake home through CLAUDE_CONFIG_DIR. The login is borrowed for the run and
        // deleted with it, so the real ~/.claude is only ever read.
        var config = Path.Combine(run, "home", ".claude");
        Directory.CreateDirectory(config);
        var login = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
        if (!File.Exists(login))
        {
            throw new GroundTruthException("No Claude Code login in ~/.claude/.credentials.json. Log in to Claude Code first.");
        }

        var borrowed = Path.Combine(config, ".credentials.json");
        File.Copy(login, borrowed);
        try
        {
            var log = Path.Combine(run, "instructions-loaded");
            Directory.CreateDirectory(log);
            var settings = Path.Combine(run, "recorder-settings.json");
            var hook = new JsonObject { ["type"] = "command", ["command"] = HookCommand(log) };
            File.WriteAllText(settings, new JsonObject { ["hooks"] = new JsonObject { ["InstructionsLoaded"] = new JsonArray { new JsonObject { ["hooks"] = new JsonArray { hook } } } } }.ToJsonString());

            var launch = Launch(scenario, run);
            var target = Path.GetRelativePath(launch, Path.Combine(run, "repo", scenario.Target)).Replace(Path.DirectorySeparatorChar, '/');
            var output = Run(
                "claude",
                ["-p", $"Read {target} with the Read tool, then reply with the single word done.", "--model", "haiku", "--settings", settings, "--permission-mode", "acceptEdits", "--output-format", "json"],
                launch,
                new() { ["CLAUDE_CONFIG_DIR"] = config });
            var session = JsonNode.Parse(output)!["session_id"]!.GetValue<string>();
            var transcript = Directory.EnumerateFiles(Path.Combine(config, "projects"), $"{session}.jsonl", SearchOption.AllDirectories).Single();
            var hookLog = Directory.EnumerateFiles(log).Select(File.ReadAllText);
            var (atLaunch, onRead) = ClaudeTranscript.Parse(File.ReadLines(transcript), hookLog, run);
            return new JsonObject
            {
                ["version"] = Version("claude", last: false),
                ["launch"] = Entries(atLaunch),
                ["read"] = Entries(onRead),
            };
        }
        finally
        {
            File.Delete(borrowed);
        }
    }

    // Scenarios run outside the home folder: Claude Code walks down from the filesystem root and would
    // read the real ~/.claude/CLAUDE.md as a project file on the way.
    private static string RunDirectory(string name)
    {
        var root = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "axm-ground-truth")
            : "/tmp/axm-ground-truth";
        var run = Path.Combine(root, $"{name}-{Guid.NewGuid().ToString("N")[..8]}");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Path.GetFullPath(run).StartsWith(Path.GetFullPath(home) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new GroundTruthException($"The run folder {run} is inside the home folder, so Claude Code would read the real ~/.claude.");
        }

        Directory.CreateDirectory(run);
        return run;
    }

    private static string Launch(Scenario scenario, string run) => Path.GetFullPath(Path.Combine(run, "repo", scenario.Launch));

    // The hook runs this recorder again, in hook-log mode, and forward slashes survive whichever shell runs it.
    private static string HookCommand(string log)
    {
        var process = Environment.ProcessPath!;
        var self = Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? $"\"{Slashes(process)}\" \"{Slashes(typeof(Recorder).Assembly.Location)}\""
            : $"\"{Slashes(process)}\"";
        return $"{self} hook-log \"{Slashes(log)}\"";
    }

    private static string Slashes(string path) => path.Replace('\\', '/');

    private static string Version(string program, bool last)
    {
        var words = Run(program, ["--version"], Environment.CurrentDirectory, []).Trim().Split(' ');
        return last ? words[^1] : words[0];
    }

    private static JsonArray Entries(IEnumerable<LoadedFile> files)
    {
        var entries = new JsonArray();
        foreach (var file in files)
        {
            var entry = new JsonObject { ["file"] = file.File, ["bytes"] = file.Bytes };
            if (file.Scope is not null)
            {
                entry["scope"] = file.Scope;
            }

            if (file.Reason is not null)
            {
                entry["reason"] = file.Reason;
            }

            if (file.Cut)
            {
                entry["cut"] = true;
            }

            entries.Add(entry);
        }

        return entries;
    }

    // A stable key order keeps re-recordings' diffs to what changed.
    private static JsonObject Ordered(JsonObject recording)
    {
        var ordered = new JsonObject();
        foreach (var key in new[] { "recorded", ClaudeCode, Codex }.Where(recording.ContainsKey))
        {
            ordered[key] = recording[key]!.DeepClone();
        }

        return ordered;
    }

    private static string Run(string program, IReadOnlyList<string> arguments, string directory, Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(program)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new GroundTruthException($"Couldn't start {program}.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(4)))
        {
            process.Kill(entireProcessTree: true);
            throw new GroundTruthException($"{program} didn't finish within 4 minutes.");
        }

        if (process.ExitCode != 0)
        {
            var tail = error.Result.Length > 600 ? error.Result[^600..] : error.Result;
            throw new GroundTruthException($"{program} {string.Join(' ', arguments.Take(2))} exited with {process.ExitCode}: {tail.Trim()}");
        }

        return output.Result;
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        if (!Directory.Exists(from))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    // Harness processes can hold files for a moment after they exit.
    private static void DeleteDirectory(string directory)
    {
        for (var attempt = 1; Directory.Exists(directory); attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(directory, recursive: true);
            }
            catch (Exception) when (attempt < 10)
            {
                Thread.Sleep(500);
            }
        }
    }
}
