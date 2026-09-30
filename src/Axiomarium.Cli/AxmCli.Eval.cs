using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

public static partial class AxmCli
{
    private static Command EvalCommand(Session session)
    {
        var command = new Command("eval", "Run the vault's behavioral and regression evals on the real harnesses, in a sealed home.");
        command.Subcommands.Add(EvalRunCommand(session));
        return command;
    }

    private static Command EvalRunCommand(Session session)
    {
        var assets = new Argument<string[]>("asset")
        {
            Description = "The assets to run, by name, or by folder such as skills/deploy when two share a name. Defaults to every asset with eval cases.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The vault is here or at the repo root above it. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var harness = new Option<string>("--harness") { Description = "Only this harness.", DefaultValueFactory = _ => "all" };
        harness.AcceptOnlyFromAmong("all", Harness.ClaudeCode.Name(), Harness.Codex.Name());
        var runs = new Option<int>("--runs") { Description = "How many times each case runs on each harness.", DefaultValueFactory = _ => 3 };
        var model = new Option<string?>("--model") { Description = "The model each harness runs. Defaults to the one you chose for it." };
        var json = new Option<bool>("--json") { Description = "Print the result as JSON, shape 1." };
        var judgeWith = new Option<string>("--judge-with")
        {
            Description = "The harness whose model grades each run against its case's rubric, as model judgment. Defaults to Claude Code.",
            DefaultValueFactory = _ => Harness.ClaudeCode.Name(),
        };
        judgeWith.AcceptOnlyFromAmong(Harness.ClaudeCode.Name(), Harness.Codex.Name());

        var command = new Command("run", "Run each asset's eval cases on the real harnesses, and report how many runs passed and what they used.");
        command.Arguments.Add(assets);
        command.Options.Add(folder);
        command.Options.Add(harness);
        command.Options.Add(runs);
        command.Options.Add(model);
        command.Options.Add(json);
        command.Options.Add(judgeWith);
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
                : EvalRun(
                    session, Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory), result.GetValue(assets) ?? [], harnesses, result.GetValue(runs),
                    result.GetValue(model), result.GetValue(json), result.GetValue(judgeWith) == Harness.Codex.Name() ? Harness.Codex : Harness.ClaudeCode);
        });
        return command;
    }

    private static int EvalRun(Session session, string start, string[] assets, Harness[] harnesses, int runs, string? model, bool json, Harness judgeWith)
    {
        var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
        var setup = EvalRuns.Plan(start, assets, harnesses, runs, machine);
        if (setup.Plan is not { } plan)
        {
            return CouldNotRunWith(session, setup.Problem!, setup.Hint);
        }

        var (versions, missing) = HarnessVersions(session, [.. plan.Sessions.Select(item => item.Harness).Distinct()], plan.RepoRoot);
        var sessions = plan.Sessions.Where(item => versions.ContainsKey(item.Harness)).ToList();
        if (sessions.Count == 0)
        {
            return CouldNotRunWith(session, $"No harness to run on. {string.Join(" ", missing.Values)}", "Install Claude Code or Codex and log in, then run this again.");
        }

        // A rubric is graded only when the judge's harness can run. The checks still decide each run.
        Harness? judge = null;
        var notes = plan.Notes.ToList();
        if (sessions.Any(item => item.Case.Rubric is not null))
        {
            var missingJudge = versions.ContainsKey(judgeWith) ? null : HarnessVersions(session, [judgeWith], plan.RepoRoot).Missing.GetValueOrDefault(judgeWith);
            if (missingJudge is null)
            {
                judge = judgeWith;
            }
            else
            {
                notes.Add($"{ExplainText.Title(judgeWith)} judges the rubrics, and it can't run, so none was graded: {missingJudge}");
            }
        }

        if (!json)
        {
            EvalText.WritePlan(session.Output, sessions, notes, judge, session.OutputStyle);
            session.Output.Flush();
        }

        var platform = OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        var ran = EvalSessions.RunAsync(
            session.Runner, sessions, plan.VaultRoot, machine, session.Environment, Environment.ProcessPath ?? "axm", model, session.Clock, platform, judge)
            .GetAwaiter().GetResult();
        if (ran.Problem is not null)
        {
            return CouldNotRunWith(session, ran.Problem, ran.Hint);
        }

        var report = new EvalReport(
            session.Clock.GetUtcNow(),
            Version,
            [.. versions.OrderBy(item => item.Key).Select(item => new EvalHarness(
                item.Key,
                item.Value,
                model ?? ran.Results.FirstOrDefault(result => result.Spec.Harness == item.Key && result.Record?.Model is not null)?.Record!.Model ?? ran.Models.GetValueOrDefault(item.Key)))],
            missing,
            ran.Warmup,
            ran.Leftovers,
            notes,
            ran.Results,
            sessions.Select(item => item.Asset).DistinctBy(asset => asset.Folder)
                .ToDictionary(asset => asset.Folder, asset => EvalHashes.Asset(Path.Combine(plan.VaultRoot, asset.Folder))),
            sessions.Select(item => item.CaseFolder).Distinct().ToDictionary(caseFolder => caseFolder, EvalHashes.Case))
        {
            Judge = judge,
        };
        var saved = SaveHistory(plan.RepoRoot, report);
        if (json)
        {
            EvalJson.Write(session.Output, report);
        }
        else
        {
            EvalText.WriteResults(session.Output, report, saved, session.OutputStyle);
        }

        // Runs are the model's behavior, not errors in the vault, so the command passes whenever it ran.
        return Passed;
    }

    // Each asset's slice of the run, as --json prints it, in .axm/evals/<asset folder>/<time>.json at the repo root.
    // It's the only thing eval writes in the repo.
    private static List<string> SaveHistory(string repoRoot, EvalReport report)
    {
        var saved = new List<string>();
        foreach (var asset in report.Results.Select(result => result.Spec.Asset.Folder).Distinct())
        {
            var folder = Path.Combine(repoRoot, ".axm", "evals", asset.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folder);
            var name = report.Date.UtcDateTime.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, $"{name}.json");
            for (var copy = 2; File.Exists(path); copy++)
            {
                path = Path.Combine(folder, $"{name}-{copy}.json");
            }

            using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                EvalJson.Write(writer, report, asset);
            }

            saved.Add(Path.GetRelativePath(repoRoot, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        return saved;
    }

    // A harness that isn't installed is skipped, and the report says so, rather than failing every session.
    private static (Dictionary<Harness, string> Versions, Dictionary<Harness, string> Missing) HarnessVersions(Session session, IReadOnlyList<Harness> harnesses, string folder)
    {
        var versions = new Dictionary<Harness, string>();
        var missing = new Dictionary<Harness, string>();
        foreach (var harness in harnesses)
        {
            var command = harness == Harness.ClaudeCode ? "claude" : "codex";
            var output = session.Runner.RunAsync(new HarnessCall(command, ["--version"], "", folder, null, TimeSpan.FromMinutes(1))).GetAwaiter().GetResult();
            if (output.Started && output.Lines.FirstOrDefault(line => line.Trim().Length > 0) is { } version)
            {
                versions[harness] = version.Trim();
            }
            else
            {
                missing[harness] = output.Started ? $"{command} --version printed nothing" : output.Error;
            }
        }

        return (versions, missing);
    }
}
