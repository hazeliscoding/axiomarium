using System.Text.Json.Nodes;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests.GroundTruth;

// The models must reproduce what the real harnesses loaded. A failure here means a model disagrees
// with a recording: fix the model, or record again if the harness changed.
public class ScenarioReplayTests
{
    private static string ScenariosFolder => Path.Combine(RepoRoot.Path, "scenarios");

    public static TheoryData<string> Scenarios => ScenarioTests.Scenarios;

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_codex_model_matches_the_recording(string name)
    {
        using var run = Copy(name);
        var scenario = Axiomarium.GroundTruth.Scenario.Load(Path.Combine(ScenariosFolder, name));
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(ScenariosFolder, name, "expected.json")))!["codex"]!["loaded"]!.AsArray();

        var resolution = CodexModel.Resolve(
            Path.Combine(run.Root, "repo", scenario.Launch),
            Path.Combine(run.Root, "repo", scenario.Target),
            new Machine(Path.Combine(run.Root, "home"), Path.Combine(run.Root, "home", ".codex"), Path.Combine(run.Root, "home", ".claude")));

        Assert.Equal(
            recording.Select(entry => (entry!["file"]!.GetValue<string>(), entry["bytes"]!.GetValue<int>(), entry["cut"]?.GetValue<bool>() ?? false)),
            resolution.Loaded.Select(item => (Path.GetRelativePath(run.Root, item.Path).Replace('\\', '/'), item.Bytes, item.Cut)));
    }

    // A copy with the .git marker the recorder's `git init` gives each run, which a scenario can't hold.
    private static TempVault Copy(string name)
    {
        var run = new TempVault();
        var source = Path.Combine(ScenariosFolder, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            run.Write(Path.GetRelativePath(source, file).Replace('\\', '/'), File.ReadAllText(file));
        }

        return run.Folder("repo/.git").Folder("home/.codex");
    }
}
