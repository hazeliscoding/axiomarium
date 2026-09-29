using System.CommandLine;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

public static partial class AxmCli
{
    private static Command TestCommand(Session session)
    {
        var skills = new Argument<string[]>("skill") { Description = "The vault skills to test. Defaults to every one with trigger prompts.", Arity = ArgumentArity.ZeroOrMore };
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The harnesses launch at the repo root above it, and the vault is here or there. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var harness = new Option<string>("--harness") { Description = "Only this harness.", DefaultValueFactory = _ => "all" };
        harness.AcceptOnlyFromAmong("all", Harness.ClaudeCode.Name(), Harness.Codex.Name());
        var runs = new Option<int>("--runs") { Description = "How many times each prompt runs on each harness.", DefaultValueFactory = _ => 3 };
        var model = new Option<string?>("--model") { Description = "The model each harness picks with. Defaults to the one it's set to." };

        var command = new Command("test", "Run each vault skill's trigger prompts on the real harnesses, and report which skill each run picked.");
        command.Arguments.Add(skills);
        command.Options.Add(folder);
        command.Options.Add(harness);
        command.Options.Add(runs);
        command.Options.Add(model);
        command.SetAction(result =>
        {
            Harness[] harnesses = result.GetValue(harness) switch
            {
                "claude-code" => [Harness.ClaudeCode],
                "codex" => [Harness.Codex],
                _ => [Harness.ClaudeCode, Harness.Codex],
            };
            return result.GetValue(runs) < 1
                ? CouldNotRunWith(session, "--runs must be 1 or more.", null)
                : Test(session, Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory), result.GetValue(skills) ?? [], harnesses, result.GetValue(runs), result.GetValue(model));
        });
        return command;
    }

    private static int Test(Session session, string start, string[] skills, Harness[] harnesses, int runs, string? model)
    {
        var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
        var setup = TriggerTest.Plan(start, skills, harnesses, runs, machine);
        if (setup.Plan is not { } plan)
        {
            return CouldNotRunWith(session, setup.Problem!, setup.Hint);
        }

        // A harness that isn't installed is skipped, and the report says so, rather than failing every session.
        var versions = new Dictionary<Harness, string>();
        var missing = new Dictionary<Harness, string>();
        foreach (var harness in plan.Sessions.Select(item => item.Harness).Distinct())
        {
            var command = harness == Harness.ClaudeCode ? "claude" : "codex";
            var output = session.Runner.RunAsync(new HarnessCall(command, ["--version"], "", plan.RepoRoot, null, TimeSpan.FromMinutes(1))).GetAwaiter().GetResult();
            if (output.Started && output.Lines.FirstOrDefault(line => line.Trim().Length > 0) is { } version)
            {
                versions[harness] = version.Trim();
            }
            else
            {
                missing[harness] = output.Started ? $"{command} --version printed nothing" : output.Error;
            }
        }

        var sessions = plan.Sessions.Where(item => versions.ContainsKey(item.Harness)).ToList();
        if (sessions.Count == 0)
        {
            return CouldNotRunWith(session, $"No harness to test on. {string.Join(" ", missing.Values)}", "Install Claude Code or Codex and log in, then run this again.");
        }

        TriggersText.WriteTestPlan(session.Output, plan, sessions, session.OutputStyle);
        session.Output.Flush();

        var listings = new Dictionary<Harness, Resolution>();
        var results = TriggerSessions.RunAsync(session.Runner, plan.Workspace, plan.RepoRoot, sessions, model, machine.Home, copy =>
        {
            var explanation = Explainer.Explain(Path.Combine(copy, "axm-probe"), copy, [.. versions.Keys.Order()], machine, copy, repoRoot: copy);
            foreach (var harness in explanation.Harnesses)
            {
                listings[harness.Harness] = harness.Resolution;
            }
        }).GetAwaiter().GetResult();

        var scored = TriggerScoring.Score(results, plan.Prompts);
        scored = scored with { Problems = TriggerCauses.Explain(scored.Problems, listings) };
        TriggersText.WriteTestResults(session.Output, scored, results, versions, missing, model, session.OutputStyle);

        // Picks are the model's behavior, not errors in the vault, so the test passes whenever it ran.
        return Passed;
    }
}
