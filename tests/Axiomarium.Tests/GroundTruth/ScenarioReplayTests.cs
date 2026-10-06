using System.Text.Json.Nodes;
using Axiomarium.Core.Instructions;
using Axiomarium.GroundTruth;

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
        ["claude-code/invalid-frontmatter"] = "session_start",
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
                    item.Rule == ClaudeCodeRules.InvalidFrontmatter && item.Timing == LoadTiming.OnRead ? "nested_traversal" : RecordedReason[item.Rule.Id],
                    item.Bytes)));
        }
    }

    // Over budget, Claude Code drops descriptions by how often each skill was used, which the model doesn't
    // guess, so then only the names and the listing's size are compared.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_claude_code_skills_match_the_recording(string name)
    {
        using var run = Copy(name);
        var scenario = Axiomarium.GroundTruth.Scenario.Load(Path.Combine(ScenariosFolder, name));
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(ScenariosFolder, name, "expected.json")))!["claude-code"]!["skills"]!;
        var overBudget = recording["overBudget"];

        var resolution = ClaudeCodeModel.Resolve(
            Path.Combine(run.Root, "repo", scenario.Launch),
            Path.Combine(run.Root, "repo", scenario.Target),
            TestMachine.For(run.Root));

        foreach (var (timing, key) in new[] { (LoadTiming.AtLaunch, "launch"), (LoadTiming.OnRead, "read") })
        {
            Assert.Equal(
                recording[key]!.AsArray().Select(entry => (
                    entry!["name"]!.GetValue<string>(),
                    entry["file"]?.GetValue<string>(),
                    overBudget is null ? entry["entry"]!.GetValue<string>() : null,
                    overBudget is null ? entry["chars"]!.GetValue<int>() : 0)),
                resolution.Skills.Where(skill => skill.Timing == timing).Select(skill => (
                    skill.Name,
                    skill.Path is null ? null : TestMachine.Relative(run.Root, skill.Path),
                    overBudget is null ? skill.NameOnly ? "name-only" : skill.Cut ? "cut" : "whole" : null,
                    overBudget is null ? skill.Chars : 0)));
        }

        Assert.Equal(overBudget?["chars"]?.GetValue<int>(), resolution.Listing!.OverBudget ? resolution.Listing.Size : null);
    }

    // Codex's bundled skills come with the harness, not the scenario, so the recording's are left out.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_codex_skills_match_the_recording(string name)
    {
        using var run = Copy(name);
        var scenario = Axiomarium.GroundTruth.Scenario.Load(Path.Combine(ScenariosFolder, name));
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(ScenariosFolder, name, "expected.json")))!["codex"]!["skills"]!.AsArray();

        var resolution = CodexModel.Resolve(
            Path.Combine(run.Root, "repo", scenario.Launch),
            Path.Combine(run.Root, "repo", scenario.Target),
            TestMachine.For(run.Root));

        Assert.Equal(
            recording
                .Where(entry => entry!["bundled"] is null)
                .Select(entry => (entry!["name"]!.GetValue<string>(), entry["file"]!.GetValue<string>(), entry["entry"]!.GetValue<string>())),
            resolution.Skills.Select(skill => (skill.Name, TestMachine.Relative(run.Root, skill.Path!), skill.Cut ? "cut" : "whole")));
    }

    // The recording holds the hooks that ran: session start always, and around the edit when the agent
    // edited. Hooks on a read aren't modeled.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_claude_code_hooks_that_ran_match_the_recording(string name)
    {
        using var run = Copy(name);
        var scenario = Axiomarium.GroundTruth.Scenario.Load(Path.Combine(ScenariosFolder, name));
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(ScenariosFolder, name, "expected.json")))!["claude-code"]!["hooks"]!.AsArray();
        var modeled = scenario.Action == Axiomarium.GroundTruth.Scenario.Edit ? new[] { "SessionStart", "PreToolUse", "PostToolUse" } : ["SessionStart"];

        var resolution = ClaudeCodeModel.Resolve(
            Path.Combine(run.Root, "repo", scenario.Launch),
            Path.Combine(run.Root, "repo", scenario.Target),
            TestMachine.For(run.Root));

        static bool Modeled(string hook) =>
            hook.StartsWith("SessionStart:", StringComparison.Ordinal) || hook.EndsWith(":Edit", StringComparison.Ordinal) || hook.EndsWith(":Write", StringComparison.Ordinal);
        Assert.Equal(
            recording
                .Select(entry => (File: entry!["file"]!.GetValue<string>(), Label: entry["label"]!.GetValue<string>(), Hook: entry["hook"]!.GetValue<string>()))
                .Where(entry => modeled.Contains(entry.Hook.Split(':')[0]) && Modeled(entry.Hook)),
            resolution.Hooks
                .Where(hook => hook.Runs && modeled.Contains(hook.Hook.Event))
                .Select(hook => (Label(run.Root, hook.Hook), Hook: $"{hook.Hook.Event}:{hook.Input}"))
                .Select(entry => (entry.Item1.File, entry.Item1.Label, entry.Hook))
                .OrderBy(entry => Array.IndexOf(["SessionStart", "PreToolUse", "PostToolUse"], entry.Hook.Split(':')[0]))
                .ThenBy(entry => entry.Hook, StringComparer.Ordinal)
                .ThenBy(entry => entry.File, StringComparer.Ordinal)
                .ThenBy(entry => entry.Label, StringComparer.Ordinal));
    }

    // hooks/list shows what Codex loads: not a project's hooks while it's untrusted, and not handlers it skips.
    [Theory]
    [MemberData(nameof(Scenarios))]
    public void The_codex_hooks_match_the_recording(string name)
    {
        using var run = Copy(name);
        var scenario = Axiomarium.GroundTruth.Scenario.Load(Path.Combine(ScenariosFolder, name));
        var recording = JsonNode.Parse(File.ReadAllText(Path.Combine(ScenariosFolder, name, "expected.json")))!["codex"]!["hooks"]!.AsArray();

        var resolution = CodexModel.Resolve(
            Path.Combine(run.Root, "repo", scenario.Launch),
            Path.Combine(run.Root, "repo", scenario.Target),
            TestMachine.For(run.Root));

        HarnessRule[] notLoaded = [CodexHookRules.ProjectUntrusted, CodexHookRules.HandlerSkipped, CodexHookRules.MatcherInvalid];
        Assert.Equal(
            recording.Select(entry => (
                entry!["file"]!.GetValue<string>(),
                entry["label"]!.GetValue<string>(),
                entry["event"]!.GetValue<string>(),
                entry["matcher"]?.GetValue<string>(),
                entry["trust"]!.GetValue<string>(),
                entry["hash"]!.GetValue<string>(),
                entry["enabled"]?.GetValue<bool>() ?? true)),
            resolution.ConfiguredHooks
                .Where(hook => !notLoaded.Contains(hook.Blocked))
                .Select(hook => (
                    Label(run.Root, hook).File,
                    Label(run.Root, hook).Label,
                    char.ToLowerInvariant(hook.Event[0]) + hook.Event[1..],
                    hook.Matcher,
                    hook.Trust!,
                    hook.Hash!,
                    hook.Blocked != CodexHookRules.Disabled)));
    }

    // A scenario hook's command is "echo MARKER <file> <label>".
    private static (string File, string Label) Label(string root, ConfiguredHook hook)
    {
        var parts = hook.Handler.Split(' ', 4);
        return parts is ["echo", "MARKER", var file, var label] ? (file, label) : (TestMachine.Relative(root, hook.Path), hook.Handler);
    }

    // A copy with the .git marker the recorder's `git init` gives each run, which a scenario can't hold, and
    // with {run} filled in, as the recorder does.
    internal static TempVault Copy(string name)
    {
        var run = new TempVault();
        var source = Path.Combine(ScenariosFolder, name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            run.Write(Path.GetRelativePath(source, file).Replace('\\', '/'), RunFolder.FillIn(File.ReadAllText(file), run.Root));
        }

        return run.Folder("repo/.git").Folder("home/.codex");
    }
}
