using System.CommandLine;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Cli;

public static partial class AxmCli
{
    private static Command ExportCommand(Session session)
    {
        var skill = new Argument<string>("skill") { Description = "The vault skill whose trigger prompts to export." };
        var folder = new Option<string>("--root")
        {
            Description = "Where to start. The vault is here or at the repo root above it. Defaults to the current directory.",
            DefaultValueFactory = _ => session.CurrentDirectory,
        };
        var format = new Option<string>("--format") { Description = "skill-creator for its {query, should_trigger} list, or promptfoo for a promptfooconfig.yaml.", Required = true };
        format.AcceptOnlyFromAmong("skill-creator", "promptfoo");

        var command = new Command("export", "Print a vault skill's trigger prompts in a format other skill-eval tools run.");
        command.Arguments.Add(skill);
        command.Options.Add(folder);
        command.Options.Add(format);
        command.SetAction(result =>
        {
            var start = Path.GetFullPath(result.GetValue(folder)!, session.CurrentDirectory);
            var name = result.GetValue(skill)!;
            var machine = session.Machine ?? Machine.FromEnvironment(session.Environment, start);
            var setup = TriggerTest.Plan(start, [name], [Harness.ClaudeCode, Harness.Codex], 1, machine);
            if (setup.Plan is not { } plan)
            {
                return CouldNotRunWith(session, setup.Problem!, setup.Hint);
            }

            var file = plan.Prompts[name];

            // The export is a file for another tool, so it's printed as is: no header, no color.
            session.Output.Write(result.GetValue(format) == "promptfoo"
                ? TriggerExport.Promptfoo(file, [.. plan.Sessions.Select(item => item.Harness).Distinct()])
                : TriggerExport.SkillCreator(file));
            return Passed;
        });
        return command;
    }
}
