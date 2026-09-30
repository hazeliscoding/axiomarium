using System.CommandLine;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Judging;

namespace Axiomarium.Cli;

public static partial class AxmCli
{
    private static Command ConflictsCommand(Session session)
    {
        var target = new Argument<string>("path") { Description = "The file whose instructions to judge. It needn't exist yet, but its folder must." };
        var judge = new Option<bool>("--judge") { Description = "Ask a model which of the file's instructions contradict each other. Nothing is judged without it." };
        var judgeWith = new Option<string>("--judge-with") { Description = "The harness whose model judges.", DefaultValueFactory = _ => Harness.ClaudeCode.Name() };
        judgeWith.AcceptOnlyFromAmong(Harness.ClaudeCode.Name(), Harness.Codex.Name());
        var harness = new Option<string>("--harness") { Description = "Only the instructions this harness loads.", DefaultValueFactory = _ => "all" };
        harness.AcceptOnlyFromAmong("all", Harness.ClaudeCode.Name(), Harness.Codex.Name());
        var cwd = new Option<string?>("--cwd") { Description = "Where the harness starts. Defaults to the repo root, or the current directory outside a repo." };
        var model = new Option<string?>("--model") { Description = "The model that judges. Defaults to the one the judging harness is set to." };

        var command = new Command("conflicts", "Have a model find instructions a file gets that contradict each other, each quoted from its file.");
        command.Arguments.Add(target);
        command.Options.Add(judge);
        command.Options.Add(judgeWith);
        command.Options.Add(harness);
        command.Options.Add(cwd);
        command.Options.Add(model);
        command.SetAction(result =>
        {
            // Deterministic checks between instructions are findings, which explain and doctor show without a model.
            if (!result.GetValue(judge))
            {
                return CouldNotRunWith(session, "axm conflicts asks a model, and only with --judge.", "Run it with --judge, or run axm explain for what needs no model.");
            }

            Harness[] harnesses = result.GetValue(harness) switch
            {
                "claude-code" => [Harness.ClaudeCode],
                "codex" => [Harness.Codex],
                _ => [Harness.ClaudeCode, Harness.Codex],
            };
            var path = result.GetValue(target)!;
            var file = Path.GetFullPath(path, session.CurrentDirectory);
            if (Directory.Exists(file))
            {
                return CouldNotRunWith(session, $"{path} is a folder.", "Pass a file in it. The file needn't exist yet.");
            }

            var folders = new[] { Path.GetDirectoryName(file)!, result.GetValue(cwd) is { } launch ? Path.GetFullPath(launch, session.CurrentDirectory) : null };
            if (folders.OfType<string>().FirstOrDefault(folder => !Directory.Exists(folder)) is { } missing)
            {
                return CouldNotRunWith(session, $"The folder {missing} doesn't exist.", hint: null);
            }

            var judgeHarness = result.GetValue(judgeWith) == Harness.Codex.Name() ? Harness.Codex : Harness.ClaudeCode;
            return Conflicts(session, file, result.GetValue(cwd), harnesses, judgeHarness, result.GetValue(model));
        });
        return command;
    }

    private static int Conflicts(Session session, string file, string? cwd, Harness[] harnesses, Harness judge, string? model)
    {
        var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, session.CurrentDirectory);
        var explanation = Explainer.Explain(file, cwd, harnesses, machine, session.CurrentDirectory);
        var repoRoot = explanation.RepoRoot ?? explanation.Launch;

        // Harnesses that load the same files are judged together, once.
        var groups = new List<(List<Harness> Harnesses, IReadOnlyList<JudgedFile> Files, ConflictAnswer? Answer)>();
        foreach (var resolution in explanation.Harnesses)
        {
            var files = ConflictJudge.Files(resolution.Resolution, repoRoot, machine.Home);
            var same = groups.FindIndex(group => group.Files.SequenceEqual(files));
            if (same >= 0)
            {
                groups[same].Harnesses.Add(resolution.Harness);
            }
            else
            {
                groups.Add(([resolution.Harness], files, null));
            }
        }

        string? judgeModel = model;
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Files.Count == 0)
            {
                continue;
            }

            string? problem = null;
            ConflictAnswer? answer = null;
            for (var attempt = 0; attempt < 2 && answer is null; attempt++)
            {
                var asked = JudgeCalls.AskAsync(session.Runner, judge, ConflictJudge.Brief(groups[i].Files, problem), model).GetAwaiter().GetResult();
                if (asked.Text is null)
                {
                    return CouldNotRunWith(session, $"The judge didn't answer: {asked.Problem}.", null);
                }

                judgeModel ??= asked.Model;
                var read = ConflictJudge.Read(asked.Text, groups[i].Files);
                problem = read.Problem;
                answer = read.Problem is null ? read : null;
            }

            if (answer is null)
            {
                return CouldNotRunWith(session, $"The judge's answer couldn't be used, twice: {problem}.", null);
            }

            groups[i] = groups[i] with { Answer = answer };
        }

        ConflictsText.Write(session.Output, DisplayPath.Of(file, repoRoot, machine.Home), judge, judgeModel, groups, session.OutputStyle);

        // Contradictions are the model's judgment, not errors in the repo, so the command passes whenever it ran.
        return Passed;
    }
}
