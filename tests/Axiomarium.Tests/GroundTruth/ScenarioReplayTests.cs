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
            TestMachine.For(run.Root));

        Assert.Equal(
            recording.Select(entry => (entry!["file"]!.GetValue<string>(), entry["bytes"]!.GetValue<int>(), entry["cut"]?.GetValue<bool>() ?? false)),
            resolution.Loaded.Select(item => (Path.GetRelativePath(run.Root, item.Path).Replace('\\', '/'), item.Bytes, item.Cut)));
    }

    // The InstructionsLoaded hook's load_reason for each rule. A rule missing here fails the test on purpose.
    private static readonly Dictionary<string, string> RecordedReason = new()
    {
        ["claude-code/managed-memory"] = "session_start",
        ["claude-code/user-memory"] = "session_start",
        ["claude-code/user-rule"] = "session_start",
        ["claude-code/ancestor-memory"] = "session_start",
        ["claude-code/ancestor-rule"] = "session_start",
        ["claude-code/local-memory"] = "session_start",
        ["claude-code/nested-memory"] = "nested_traversal",
        ["claude-code/nested-rule"] = "nested_traversal",
        ["claude-code/import"] = "include",
        ["claude-code/path-rule"] = "path_glob_match",
        ["claude-code/agents-md"] = "agents-md",
        ["claude-code/nested-agents-md"] = "agents-md",
    };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_claude_code_model_matches_the_recording(string name)
    {
        using var run = Copy(name);
        var scenario = Axiomarium.GroundTruth.Scenario.Load(Path.Combine(ScenariosFolder, name));
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(ScenariosFolder, name, "expected.json")))!["claude-code"]!;

        var resolution = ClaudeCodeModel.Resolve(
            Path.Combine(run.Root, "repo", scenario.Launch),
            Path.Combine(run.Root, "repo", scenario.Target),
            TestMachine.For(run.Root));

        foreach (var (timing, key) in new[] { (LoadTiming.AtLaunch, "launch"), (LoadTiming.OnRead, "read") })
        {
            Assert.Equal(
                recording[key]!.AsArray().Select(entry => (
                    entry!["file"]!.GetValue<string>(),
                    entry["scope"]!.GetValue<string>(),
                    entry["reason"]!.GetValue<string>(),
                    entry["bytes"]!.GetValue<int>())),
                resolution.Loaded.Where(item => item.Timing == timing).Select(item => (
                    TestMachine.Relative(run.Root, item.Path),
                    item.Scope.ToString(),
                    RecordedReason[item.Rule.Id],
                    item.Bytes)));
        }
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
