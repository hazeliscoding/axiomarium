using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

// Every scenario has to be well formed before its recording can be trusted.
public class ScenarioTests
{
    private static string ScenariosFolder => Path.Combine(RepoRoot.Path, "scenarios");

    public static TheoryData<string> Scenarios =>
        new(Directory.GetDirectories(ScenariosFolder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal));

    private static Scenario Load(string name) => Scenario.Load(Path.Combine(ScenariosFolder, name));

    [Fact]
    public void There_are_scenarios()
    {
        Assert.NotEmpty(Directory.GetDirectories(ScenariosFolder));
    }

    // Markers go first, so a file Codex cuts short can still be named.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_markdown_file_starts_with_its_marker(string name)
    {
        foreach (var (file, content) in Load(name).MarkdownFiles().Where(pair => !string.IsNullOrWhiteSpace(pair.Value)))
        {
            Assert.Equal($"MARKER {file}", Scenario.FirstBodyLine(content));
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_launch_folder_and_the_target_exist(string name)
    {
        var scenario = Load(name);

        Assert.True(Directory.Exists(Path.Combine(scenario.Directory, "repo", scenario.Launch)), $"{name}: no launch folder {scenario.Launch}");
        Assert.True(File.Exists(Path.Combine(scenario.Directory, "repo", scenario.Target)), $"{name}: no target {scenario.Target}");
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_recording_names_only_the_scenarios_files_and_both_versions(string name)
    {
        var scenario = Load(name);
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(scenario.Directory, Scenario.RecordingFile)))!;
        var files = scenario.MarkdownFiles();

        foreach (var harness in new[] { "claude-code", "codex" })
        {
            Assert.False(string.IsNullOrEmpty(recording[harness]?["version"]?.GetValue<string>()), $"{name}: no {harness} version");
        }

        var named = new[] { recording["claude-code"]!["launch"], recording["claude-code"]!["read"], recording["codex"]!["loaded"] }
            .SelectMany(list => list!.AsArray())
            .Select(entry => entry!["file"]!.GetValue<string>());
        Assert.All(named, file => Assert.True(files.ContainsKey(file), $"{name}: the recording names {file}, which isn't in the scenario"));
    }
}
