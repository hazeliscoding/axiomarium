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

    // CI records Codex, so the version it installs is the one the model is confirmed against.
    [Fact]
    public void The_codex_workflow_records_the_version_the_model_is_confirmed_against()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot.Path, ".github", "workflows", "record-codex.yml"));

        Assert.Contains($"@openai/codex@{Axiomarium.Core.Instructions.CodexModel.ConfirmedWith}", workflow);
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

    // A listing shows a skill's description, not its body, so the description carries the marker too.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_skill_description_starts_with_its_marker(string name)
    {
        var scenario = Load(name);
        foreach (var (file, content) in scenario.MarkdownFiles().Where(pair => Scenario.IsSkill(pair.Key)))
        {
            if (Scenario.FrontmatterValue(content, "description") is { } description)
            {
                Assert.True(
                    description == $"MARKER {file}" || description.StartsWith($"MARKER {file} ", StringComparison.Ordinal),
                    $"{name}: {file}'s description doesn't start with \"MARKER {file}\"");
            }
        }
    }

    // A recording can only name a hook by what it prints.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Every_hook_command_prints_its_marker_and_a_label_unique_in_its_file(string name)
    {
        foreach (var (file, commands) in Load(name).HookCommands())
        {
            var labels = commands.Select(command =>
            {
                Assert.StartsWith($"echo MARKER {file} ", command);
                return command[$"echo MARKER {file} ".Length..];
            }).ToList();
            Assert.All(labels, label => Assert.Matches("^[a-z0-9-]+$", label));
            Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void A_scenario_asks_the_agent_to_read_or_edit_the_target()
    {
        using var folder = new TempVault()
            .Write("scenario.yaml", "description: A test.\nlaunch: .\ntarget: a.md\naction: write\n");

        var problem = Assert.Throws<GroundTruthException>(() => Scenario.Load(folder.Root));

        Assert.Contains("action", problem.Message);
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

    // Built-in skills are named without a file; everything else a recording names is a file in the scenario.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_recorded_skills_and_hooks_are_the_scenarios_own(string name)
    {
        var scenario = Load(name);
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(scenario.Directory, Scenario.RecordingFile)))!;
        var claudeCode = recording["claude-code"]!;
        var codex = recording["codex"]!;

        var entries = new[] { claudeCode["skills"]?["launch"], claudeCode["skills"]?["read"], claudeCode["hooks"], codex["skills"], codex["hooks"] }
            .SelectMany(list => list?.AsArray() ?? [])
            .ToList();
        Assert.All(
            entries.Select(entry => entry!["file"]?.GetValue<string>()).OfType<string>(),
            file => Assert.True(File.Exists(Path.Combine(scenario.Directory, file)), $"{name}: the recording names {file}, which isn't in the scenario"));
    }
}
