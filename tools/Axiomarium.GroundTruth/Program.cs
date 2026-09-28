using Axiomarium.GroundTruth;

// Records what the real Claude Code and Codex load for each scenario, into its expected.json:
//
//   dotnet run --project tools/Axiomarium.GroundTruth -- [scenario ...] [--harness claude-code|codex]
//
// With no scenarios it records them all. Codex runs offline, on Linux or macOS only: on Windows it reads
// ~/.agents/skills from the real profile folder, so .github/workflows/record-codex.yml records it. Each
// Claude Code recording is a short Haiku session on your login, which the recorder borrows for the run
// and deletes afterwards.

if (args is ["hook-log", var logFolder])
{
    // The InstructionsLoaded hook calls the recorder back to log each load. Claude Code runs these hooks
    // in parallel, so each call writes its own file rather than appending to a shared one.
    File.WriteAllText(Path.Combine(logFolder, $"{Guid.NewGuid():N}.json"), Console.In.ReadToEnd().Trim());
    return 0;
}

var harnesses = new List<string> { Recorder.ClaudeCode, Recorder.Codex };
var names = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--harness" && i + 1 < args.Length)
    {
        harnesses = [args[++i]];
    }
    else
    {
        names.Add(args[i]);
    }
}

if (OperatingSystem.IsWindows() && harnesses.Contains(Recorder.Codex))
{
    if (harnesses.Count == 1)
    {
        Console.Error.WriteLine("Codex can't be recorded on Windows, where it reads your real ~/.agents/skills. Run .github/workflows/record-codex.yml instead.");
        return 1;
    }

    harnesses.Remove(Recorder.Codex);
    Console.WriteLine("Recording Claude Code only: .github/workflows/record-codex.yml records Codex.");
}

var folder = FindScenarios();
var directories = names.Count > 0 ? names.Select(name => Path.Combine(folder, name)) : Directory.GetDirectories(folder).Order(StringComparer.Ordinal);

var failed = 0;
foreach (var directory in directories)
{
    try
    {
        var scenario = Scenario.Load(directory);
        Recorder.Record(scenario, harnesses);
        Console.WriteLine($"RECORDED  {scenario.Name}");
    }
    catch (GroundTruthException problem)
    {
        failed++;
        Console.Error.WriteLine($"FAILED    {Path.GetFileName(directory)}: {problem.Message}");
    }
}

return failed == 0 ? 0 : 1;

static string FindScenarios()
{
    for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
    {
        var scenarios = Path.Combine(directory.FullName, "scenarios");
        if (File.Exists(Path.Combine(directory.FullName, "axiomarium.slnx")) && Directory.Exists(scenarios))
        {
            return scenarios;
        }
    }

    throw new GroundTruthException("Run the recorder inside the Axiomarium repo, which holds scenarios/.");
}
