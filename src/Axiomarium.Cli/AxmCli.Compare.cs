using System.CommandLine;
using System.Runtime.InteropServices;
using Axiomarium.Core.Assets;
using Axiomarium.Core.Evals;
using Axiomarium.Core.Health;
using Axiomarium.Core.Instructions;

namespace Axiomarium.Cli;

/// <summary>One case on one harness in a compare: its baseline side and its candidate side.</summary>
/// <param name="Case">The case.</param>
/// <param name="Harness">The harness.</param>
/// <param name="Baseline">The baseline's runs, counted.</param>
/// <param name="Candidate">The candidate's runs, counted.</param>
internal sealed record ComparedCase(EvalCase Case, Harness Harness, CaseSide Baseline, CaseSide Candidate)
{
    /// <summary>The baseline's runs in full, or <see langword="null"/> when a saved run stood in for them.</summary>
    public CaseSummary? BaselineRuns { get; init; }

    /// <summary>The candidate's runs in full.</summary>
    public CaseSummary? CandidateRuns { get; init; }
}

/// <summary>Everything <c>axm eval compare</c> reports.</summary>
/// <param name="Date">When it finished.</param>
/// <param name="Axm">The version of <c>axm</c> that ran it.</param>
/// <param name="Asset">The asset's folder.</param>
/// <param name="Ref">The baseline's git ref, or <c>none</c> for no asset.</param>
/// <param name="BaselineHash">The baseline's content hash, or <c>none</c>.</param>
/// <param name="CandidateHash">The working tree's content hash.</param>
/// <param name="Reused">The saved run the baseline reused, as a shown path, or <see langword="null"/> when it ran.</param>
/// <param name="CasesChanged">Whether the cases differ from the ref's, so both versions ran the working tree's.</param>
/// <param name="Harnesses">The harnesses it ran on.</param>
/// <param name="Skipped">Each harness it couldn't run on, and why.</param>
/// <param name="Notes">What was left out, and why.</param>
/// <param name="Cases">Each case on each harness, side by side.</param>
/// <param name="Runs">How many runs each side got.</param>
internal sealed record CompareReport(
    DateTimeOffset Date,
    string Axm,
    string Asset,
    string Ref,
    string BaselineHash,
    string CandidateHash,
    string? Reused,
    bool CasesChanged,
    IReadOnlyList<EvalHarness> Harnesses,
    IReadOnlyDictionary<Harness, string> Skipped,
    IReadOnlyList<string> Notes,
    IReadOnlyList<ComparedCase> Cases,
    int Runs);

public static partial class AxmCli
{
    private static Command EvalCompareCommand(Session session)
    {
        var asset = new Argument<string>("asset") { Description = "The asset to compare, by name, or by folder such as skills/deploy when two share a name." };
        var baseline = new Option<string>("--baseline")
        {
            Description = "What to compare the working tree with: a git ref, or none for no asset at all. Defaults to HEAD.",
            DefaultValueFactory = _ => "HEAD",
        };
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The vault is here or at the repo root above it. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var harness = new Option<string>("--harness") { Description = "Only this harness.", DefaultValueFactory = _ => "all" };
        harness.AcceptOnlyFromAmong("all", Harness.ClaudeCode.Name(), Harness.Codex.Name());
        var runs = new Option<int>("--runs") { Description = "How many times each case runs on each harness, for each version.", DefaultValueFactory = _ => 3 };
        var model = new Option<string?>("--model") { Description = "The model each harness runs. Defaults to the one you chose for it." };
        var fresh = new Option<bool>("--fresh") { Description = "Run the baseline again, even when a saved run still describes it." };
        var json = new Option<bool>("--json") { Description = "Print the result as JSON, shape 1." };
        var judgeWith = new Option<string>("--judge-with")
        {
            Description = "The harness whose model grades each run against its case's rubric, as model judgment. Defaults to Claude Code.",
            DefaultValueFactory = _ => Harness.ClaudeCode.Name(),
        };
        judgeWith.AcceptOnlyFromAmong(Harness.ClaudeCode.Name(), Harness.Codex.Name());

        var command = new Command("compare", "Run an asset's eval cases on a baseline and on the working tree, and show both side by side.");
        command.Arguments.Add(asset);
        command.Options.Add(baseline);
        command.Options.Add(folder);
        command.Options.Add(harness);
        command.Options.Add(runs);
        command.Options.Add(model);
        command.Options.Add(fresh);
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
                : Compare(
                    session, Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory), result.GetValue(asset)!, result.GetValue(baseline)!, harnesses,
                    result.GetValue(runs), result.GetValue(model), result.GetValue(fresh), result.GetValue(json),
                    result.GetValue(judgeWith) == Harness.Codex.Name() ? Harness.Codex : Harness.ClaudeCode);
        });
        return command;
    }

    private static int Compare(
        Session session, string start, string name, string gitRef, Harness[] harnesses, int runs, string? model, bool fresh, bool json, Harness judgeWith)
    {
        var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
        var setup = EvalRuns.Plan(start, [name], harnesses, runs, machine);
        if (setup.Plan is not { } plan)
        {
            return CouldNotRunWith(session, setup.Problem!, setup.Hint);
        }

        var asset = plan.Sessions[0].Asset;
        var notes = plan.Notes.ToList();
        var specs = plan.Sessions.ToList();
        if (asset.Kind == AssetKind.Agent && specs.Any(item => item.Harness == Harness.Codex))
        {
            specs.RemoveAll(item => item.Harness == Harness.Codex);
            notes.Add($"{asset.Name} runs on Claude Code only in a compare: an agent lives in the Codex home, which both versions would share.");
            if (specs.Count == 0)
            {
                return CouldNotRunWith(session, $"{asset.Name} is an agent, and a compare can't run an agent on Codex.", "Compare it on Claude Code, with --harness claude-code.");
            }
        }

        var (versions, missing) = HarnessVersions(session, [.. specs.Select(item => item.Harness).Distinct()], plan.RepoRoot);
        specs.RemoveAll(item => !versions.ContainsKey(item.Harness));
        if (specs.Count == 0)
        {
            return CouldNotRunWith(session, $"No harness to run on. {string.Join(" ", missing.Values)}", "Install Claude Code or Codex and log in, then run this again.");
        }

        var none = gitRef == "none";
        var scratch = none ? null : session.Runner.CreateFolder("evals");
        try
        {
            EvalVariant baselineVariant;
            var casesChanged = false;
            if (scratch is null)
            {
                baselineVariant = new EvalVariant("baseline", null, null);
            }
            else
            {
                var problem = MaterializeBaseline(session, plan, asset, gitRef, scratch);
                if (problem is not null)
                {
                    return CouldNotRunWith(session, problem, "Pass --baseline none to compare with no asset at all.");
                }

                var old = Doctor.Run(scratch).Report?.Assets.FirstOrDefault(item => item.Folder == asset.Folder);
                if (old?.Manifest is null)
                {
                    return CouldNotRunWith(session, $"{asset.Folder}/asset.yaml at {gitRef} has errors, so that version can't be installed.", "Compare with another ref, or with --baseline none.");
                }

                if (EvalHashes.Asset(Path.Combine(scratch, asset.Folder)) == EvalHashes.Asset(Path.Combine(plan.VaultRoot, asset.Folder)))
                {
                    return CouldNotRunWith(session, $"{asset.Folder} is unchanged since {gitRef}.", "Change it first, or compare with --baseline none.");
                }

                baselineVariant = new EvalVariant("baseline", old, scratch);
                casesChanged = specs.Select(item => item.CaseFolder).Distinct().Any(caseFolder =>
                {
                    var atRef = Path.Combine(scratch, Path.GetRelativePath(plan.VaultRoot, caseFolder));
                    return !Directory.Exists(atRef) || EvalHashes.Case(atRef) != EvalHashes.Case(caseFolder);
                });
            }

            var baselineHash = scratch is null ? "none" : EvalHashes.Asset(Path.Combine(scratch, asset.Folder));
            var candidateHash = EvalHashes.Asset(Path.Combine(plan.VaultRoot, asset.Folder));
            var caseHashes = specs.Select(item => item.CaseFolder).Distinct().ToDictionary(caseFolder => caseFolder, EvalHashes.Case);
            var models = SealedHomes.Models(machine);
            var asked = versions.ToDictionary(item => item.Key, item => (Version: item.Value, Asked: model ?? models.GetValueOrDefault(item.Key)));
            var history = Path.Combine(plan.RepoRoot, ".axm", "evals", asset.Folder.Replace('/', Path.DirectorySeparatorChar));
            var reused = fresh ? null : EvalHistory.FindBaseline(
                history,
                asset.Folder,
                baselineHash,
                specs.DistinctBy(item => item.CaseFolder).ToDictionary(item => (item.Case.Name, EvalCases.Folder(item.Case.Type)), item => caseHashes[item.CaseFolder]),
                asked.Where(item => specs.Any(spec => spec.Harness == item.Key)).ToDictionary(item => item.Key, item => item.Value),
                runs);

            // Baseline and candidate alternate, so a rate limit or a slow hour falls on both.
            var candidate = new EvalVariant("candidate", asset, plan.VaultRoot);
            var sessions = specs
                .SelectMany(item => reused is null
                    ? new[] { item with { Variant = baselineVariant }, item with { Variant = candidate } }
                    : [item with { Variant = candidate }])
                .ToList();

            Harness? judge = null;
            if (sessions.Any(item => item.Case.Rubric is not null))
            {
                var missingJudge = versions.ContainsKey(judgeWith) ? null : HarnessVersions(session, [judgeWith], plan.RepoRoot).Missing.GetValueOrDefault(judgeWith);
                judge = missingJudge is null ? judgeWith : null;
                if (missingJudge is not null)
                {
                    notes.Add($"{ExplainText.Title(judgeWith)} judges the rubrics, and it can't run, so none was graded: {missingJudge}");
                }
            }

            if (!json)
            {
                EvalCompareText.WritePlan(session.Output, asset.Folder, gitRef, specs, sessions.Count, reused, plan.RepoRoot, casesChanged, notes, session.OutputStyle);
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

            var date = session.Clock.GetUtcNow();
            var evalHarnesses = versions.Where(item => specs.Any(spec => spec.Harness == item.Key)).OrderBy(item => item.Key).Select(item => new EvalHarness(
                item.Key,
                item.Value,
                model ?? ran.Results.FirstOrDefault(result => result.Spec.Harness == item.Key && result.Record?.Model is not null)?.Record!.Model ?? ran.Models.GetValueOrDefault(item.Key),
                asked[item.Key].Asked)).ToList();
            EvalReport Side(string variant, string hash) => new(
                date, Version, evalHarnesses, missing, ran.Warmup, ran.Leftovers, notes,
                [.. ran.Results.Where(result => result.Spec.Variant?.Name == variant)],
                new Dictionary<string, string> { [asset.Folder] = hash },
                caseHashes)
            {
                Judge = judge,
                Command = "eval compare",
                Variant = variant,
                Ref = variant == "baseline" ? gitRef : null,
            };

            var saved = new List<string>();
            if (reused is null)
            {
                saved.AddRange(SaveHistory(plan.RepoRoot, Side("baseline", baselineHash), "-baseline"));
            }

            saved.AddRange(SaveHistory(plan.RepoRoot, Side("candidate", candidateHash), "-candidate"));
            var candidateSummaries = EvalSummaries.Summarize([.. ran.Results.Where(result => result.Spec.Variant?.Name == "candidate")]);
            var baselineSummaries = EvalSummaries.Summarize([.. ran.Results.Where(result => result.Spec.Variant?.Name == "baseline")]);
            var cases = candidateSummaries.Select(summary =>
            {
                var fresh = baselineSummaries.FirstOrDefault(item => item.Case == summary.Case && item.Harness == summary.Harness);
                var saved = reused?.Cases.FirstOrDefault(item => item.Case == summary.Case.Name && item.Type == EvalCases.Folder(summary.Case.Type) && item.Harness == summary.Harness);
                return new ComparedCase(summary.Case, summary.Harness, saved?.Side ?? CaseSide.Of(fresh!), CaseSide.Of(summary))
                {
                    BaselineRuns = fresh,
                    CandidateRuns = summary,
                };
            }).ToList();
            var report = new CompareReport(
                date, Version, asset.Folder, gitRef, baselineHash, candidateHash,
                reused is null ? null : Path.GetRelativePath(plan.RepoRoot, reused.File).Replace(Path.DirectorySeparatorChar, '/'),
                casesChanged, evalHarnesses, missing, notes, cases, runs);
            if (json)
            {
                EvalCompareJson.Write(session.Output, report);
            }
            else
            {
                EvalCompareText.WriteResults(session.Output, report, ran.Warmup, saved, session.OutputStyle);
            }

            // Runs are the model's behavior, not errors in the vault, so the command passes whenever it ran.
            return Passed;
        }
        finally
        {
            if (scratch is not null)
            {
                ScratchFolders.Delete(scratch);
            }
        }
    }

    // Writes the asset's folder at gitRef into scratch, where the vault's kind folders are, evals and all.
    private static string? MaterializeBaseline(Session session, EvalRunPlan plan, DiscoveredAsset asset, string gitRef, string scratch)
    {
        var folder = Path.GetRelativePath(plan.RepoRoot, Path.Combine(plan.VaultRoot, asset.Folder)).Replace(Path.DirectorySeparatorChar, '/');
        var listed = session.Runner.RunAsync(new HarnessCall("git", ["ls-tree", "-r", "--name-only", gitRef, "--", folder], "", plan.RepoRoot, null, TimeSpan.FromMinutes(1)))
            .GetAwaiter().GetResult();
        if (!listed.Started || listed.ExitCode != 0)
        {
            return $"git couldn't list {folder} at {gitRef}: {(listed.Started ? listed.Error.Trim() : listed.Error)}";
        }

        var files = listed.Lines.Where(line => line.Trim().Length > 0).ToList();
        if (files.Count == 0)
        {
            return $"{asset.Folder} doesn't exist at {gitRef}.";
        }

        foreach (var file in files)
        {
            var shown = session.Runner.RunAsync(new HarnessCall("git", ["show", $"{gitRef}:{file}"], "", plan.RepoRoot, null, TimeSpan.FromMinutes(1)))
                .GetAwaiter().GetResult();
            if (!shown.Started || shown.ExitCode != 0)
            {
                return $"git couldn't show {file} at {gitRef}: {(shown.Started ? shown.Error.Trim() : shown.Error)}";
            }

            var target = Path.Combine(scratch, asset.Folder, Path.GetRelativePath(folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, string.Join('\n', shown.Lines) + "\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return null;
    }
}
