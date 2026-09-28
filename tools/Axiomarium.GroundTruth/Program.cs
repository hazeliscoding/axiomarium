using Axiomarium.GroundTruth;

// Records what the real Claude Code and Codex load for each scenario, into its expected.json:
//
//   dotnet run --project tools/Axiomarium.GroundTruth -- [scenario ...] [--harness claude-code|codex]
//
// With no scenarios it records them all. Codex runs offline; each Claude Code recording is a short Haiku
// session on your login, which the recorder borrows for the run and deletes afterwards.

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

var folder = FindScenarios();
var scenarios = (names.Count > 0 ? names.Select(name => Path.Combine(folder, name)) : Directory.GetDirectories(folder).Order(StringComparer.Ordinal))
    .Select(Scenario.Load)
    .ToList();

var failed = 0;
foreach (var scenario in scenarios)
{
    try
    {
        Recorder.Record(scenario, harnesses);
        Console.WriteLine($"RECORDED  {scenario.Name}");
    }
    catch (GroundTruthException problem)
    {
        failed++;
        Console.Error.WriteLine($"FAILED    {scenario.Name}: {problem.Message}");
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
