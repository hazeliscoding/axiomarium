using System.CommandLine;
using System.Text;
using Axiomarium.Cli.Output;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

public static partial class AxmCli
{
    // Long enough for a slow first turn on a large model, short enough that a hung session doesn't hang axm.
    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Claude Code with tools off, MCP servers off and no saved session, as the M4 spike settled: one turn of text.</summary>
    internal static readonly string[] JudgeArguments =
        ["-p", "--tools", "", "--strict-mcp-config", "--output-format", "stream-json", "--verbose", "--no-session-persistence"];

    private static Command GenerateCommand(Session session)
    {
        var skill = new Argument<string>("skill") { Description = "The vault skill to write trigger prompts for." };
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The vault is here or at the repo root above it. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var model = new Option<string?>("--model") { Description = "The model Claude Code writes the prompts with. Defaults to the one it's set to." };
        var yes = new Option<bool>("--yes") { Description = "Write without asking. Needed when there's no terminal to ask in." };
        var replace = new Option<bool>("--replace") { Description = "Write over the skill's existing prompts." };

        var command = new Command("generate", "Have Claude Code write trigger prompts for a vault skill, show them, and write them after you approve.");
        command.Arguments.Add(skill);
        command.Options.Add(folder);
        command.Options.Add(model);
        command.Options.Add(yes);
        command.Options.Add(replace);
        command.SetAction(result => Generate(
            session, Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory), result.GetValue(skill)!, result.GetValue(model), result.GetValue(yes), result.GetValue(replace)));
        return command;
    }

    private static int Generate(Session session, string start, string skill, string? model, bool yes, bool replace)
    {
        var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
        var setup = TriggerGeneration.Plan(start, skill, machine);
        if (setup.Plan is not { } plan)
        {
            return CouldNotRunWith(session, setup.Problem!, setup.Hint);
        }

        string Show(string path) => DisplayPath.Of(path, plan.RepoRoot, machine.Home);

        // Both checks come before the model call, so a run that can't write costs nothing.
        if (File.Exists(plan.Target) && !replace)
        {
            return CouldNotRunWith(session, $"{Show(plan.Target)} already exists.", "Pass --replace to write new prompts over it.");
        }

        if (!yes && session.InputRedirected)
        {
            return CouldNotRunWith(session, "generate asks before it writes, and there's no terminal to ask in.", "Pass --yes to write without asking.");
        }

        var date = session.Clock.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string[] arguments = model is null ? JudgeArguments : [.. JudgeArguments, "--model", model];
        string? problem = null;
        TriggerPromptFile? file = null;
        ClaudeCodeSession? answered = null;
        for (var attempt = 0; attempt < 2 && file is null; attempt++)
        {
            var brief = GenerationBrief.Build(plan.Skill, plan.Text, plan.Rivals, problem);
            var run = session.Runner.RunAsync(new HarnessCall("claude", arguments, brief, null, null, GenerateTimeout)).GetAwaiter().GetResult();
            if (!run.Started)
            {
                return CouldNotRunWith(session, $"Couldn't start Claude Code: {run.Error}", "Install Claude Code and log in, then run this again.");
            }

            answered = ClaudeCodeStream.Read(run.Lines);
            var text = answered.Result ?? string.Concat(answered.Steps.Select(step => step.Text));
            if (answered.IsError || text.Length == 0)
            {
                var reason = answered.Result ?? run.Error.Trim().Split('\n').LastOrDefault(line => line.Trim().Length > 0) ?? $"it exited with {run.ExitCode}";
                return CouldNotRunWith(session, $"Claude Code stopped without an answer: {reason.Trim()}", null);
            }

            var read = answered.Steps.Any(step => step.Tool is not null)
                ? new PromptFileRead(null, [new PromptProblem(null, "the answer called a tool", [])])
                : TriggerPrompts.FromAnswer(text, plan.Skill, new PromptSource("claude-code", answered.Model ?? "unknown", date));
            file = read.File;
            problem = read.Problems.FirstOrDefault()?.Message;
        }

        if (file is null)
        {
            return CouldNotRunWith(session, $"Claude Code's answer couldn't be used, twice: {problem}. Nothing was written.", null);
        }

        TriggersText.WriteGenerated(session.Output, file, answered!.Version, session.OutputStyle);
        var ink = new Ink(session.Output, session.OutputStyle);
        if (!yes)
        {
            ink.Write($"Write {Show(plan.Target)}? [y/N] ");
            session.Output.Flush();
            if (session.Input.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes"))
            {
                ink.Write("Nothing written.").Kaomoji(Output.Kaomoji.AllClear, Palette.Ok).Line();
                return Passed;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(plan.Target)!);
        File.WriteAllText(plan.Target, TriggerPrompts.Write(file), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ink.Write($"Wrote {Show(plan.Target)}. Once you've reviewed the prompts, set evals.trigger: true in {Show(plan.Manifest)}.")
            .Kaomoji(Output.Kaomoji.AllClear, Palette.Ok).Line();
        return Passed;
    }
}
