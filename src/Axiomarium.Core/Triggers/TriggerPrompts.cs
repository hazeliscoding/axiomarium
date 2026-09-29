using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Health;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Core.Triggers;

/// <summary>What a trigger prompt tests, which decides whether it should pick the skill.</summary>
public enum PromptKind
{
    /// <summary>A plain request for what the skill does. It should pick the skill.</summary>
    Positive,

    /// <summary>The same kind of request without the description's words. It should pick the skill.</summary>
    Paraphrased,

    /// <summary>An unrelated request. It shouldn't pick the skill.</summary>
    Negative,

    /// <summary>A request that shares the skill's words but isn't for it. It shouldn't pick the skill.</summary>
    Adversarial,

    /// <summary>A request between the skill and a rival, which either could claim.</summary>
    Ambiguous,
}

/// <summary>One prompt in a skill's <c>evals/trigger/prompts.yaml</c>.</summary>
/// <param name="Prompt">What the user asks, run as the first message of a session.</param>
/// <param name="Kind">What the prompt tests.</param>
/// <param name="ShouldTrigger">Whether the agent should pick the skill.</param>
/// <param name="Rival">For an ambiguous prompt, the skill it's meant to be confused with, or <see langword="null"/>.</param>
public sealed record TriggerPrompt(string Prompt, PromptKind Kind, bool ShouldTrigger, string? Rival = null);

/// <summary>Who wrote a prompt file, when a model did.</summary>
/// <param name="By">The harness that ran the model, such as <c>claude-code</c>.</param>
/// <param name="Model">The model, as the harness names it.</param>
/// <param name="Date">When, as <c>YYYY-MM-DD</c>.</param>
public sealed record PromptSource(string By, string Model, string Date);

/// <summary>A skill's trigger prompts.</summary>
/// <param name="Skill">The skill they're for.</param>
/// <param name="Generated">Who wrote them, or <see langword="null"/> when no model did.</param>
/// <param name="Prompts">The prompts, in file order. Never empty.</param>
public sealed record TriggerPromptFile(string Skill, PromptSource? Generated, IReadOnlyList<TriggerPrompt> Prompts);

/// <summary>A problem in a prompt file.</summary>
/// <param name="Location">Where in the file, when known.</param>
/// <param name="Message">What is wrong, as one line.</param>
/// <param name="Detail">Lines that help fix it. Often empty.</param>
public sealed record PromptProblem(SourceLocation? Location, string Message, IReadOnlyList<string> Detail);

/// <summary>A prompt file as read: the prompts when the file is valid, and every problem found.</summary>
/// <param name="File">The prompts, or <see langword="null"/> when there is any problem.</param>
/// <param name="Problems">Every problem, in file order. Empty when the file is valid.</param>
public sealed record PromptFileRead(TriggerPromptFile? File, IReadOnlyList<PromptProblem> Problems);

/// <summary>Reads and checks a skill's <c>evals/trigger/prompts.yaml</c>, the prompts <c>axm triggers test</c> runs.</summary>
public static class TriggerPrompts
{
    /// <summary>Where a skill keeps its trigger prompts, relative to its folder.</summary>
    public const string RelativePath = "evals/trigger/prompts.yaml";

    /// <summary>
    /// Reads <paramref name="text"/> as a prompt file and checks it: against its schema, then that its skill
    /// matches <paramref name="folder"/>, that each prompt's <c>should_trigger</c> agrees with its kind, and
    /// that only ambiguous prompts name a rival.
    /// </summary>
    /// <param name="text">The file's contents.</param>
    /// <param name="folder">The name of the skill's folder, which the file's <c>skill</c> must match.</param>
    /// <returns>The prompts when the file is valid, and every problem otherwise.</returns>
    public static PromptFileRead Read(string text, string folder)
    {
        var parsed = YamlDocument.Parse(text);
        if (parsed.Problem is { } yaml)
        {
            return new PromptFileRead(null, [new PromptProblem(yaml.Location, yaml.Message, [])]);
        }

        var problems = SchemaValidator.Validate(parsed.Root, SchemaCatalog.TriggerPrompts)
            .Select(error => new PromptProblem(Doctor.Locate(parsed.Locations, error.Path), error.Message, error.Detail))
            .ToList();
        if (problems.Count > 0)
        {
            return new PromptFileRead(null, problems);
        }

        var root = parsed.Root!.AsObject();
        var skill = root["skill"]!.GetValue<string>();
        if (skill != folder)
        {
            problems.Add(new PromptProblem(Doctor.Locate(parsed.Locations, "skill"), $"skill \"{skill}\" doesn't match its folder \"{folder}\"", []));
        }

        var prompts = root["prompts"]!.AsArray().Select(node => node!.AsObject()).Select(Prompt).ToList();
        for (var i = 0; i < prompts.Count; i++)
        {
            var prompt = prompts[i];
            if (Expected(prompt.Kind) is { } expected && prompt.ShouldTrigger != expected)
            {
                problems.Add(new PromptProblem(
                    Doctor.Locate(parsed.Locations, $"prompts[{i}].should_trigger"),
                    $"prompts[{i}] is a{(prompt.Kind == PromptKind.Adversarial ? "n" : "")} {Name(prompt.Kind)} prompt, so should_trigger must be {(expected ? "true" : "false")}",
                    ["Positive and paraphrased prompts should pick the skill, and negative and adversarial ones shouldn't."]));
            }

            if (prompt.Rival is not null && prompt.Kind != PromptKind.Ambiguous)
            {
                problems.Add(new PromptProblem(Doctor.Locate(parsed.Locations, $"prompts[{i}].rival"), $"prompts[{i}] names a rival, which only an ambiguous prompt does", []));
            }
        }

        var generated = root["generated"] is JsonObject source
            ? new PromptSource(source["by"]!.GetValue<string>(), source["model"]!.GetValue<string>(), source["date"]!.GetValue<string>())
            : null;
        return problems.Count > 0 ? new PromptFileRead(null, problems) : new PromptFileRead(new TriggerPromptFile(skill, generated, prompts), []);
    }

    /// <summary>The name a prompt file uses for <paramref name="kind"/>, such as <c>paraphrased</c>.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>Its name in lowercase.</returns>
    public static string Name(PromptKind kind) => kind.ToString().ToLowerInvariant();

    private static TriggerPrompt Prompt(JsonObject prompt) => new(
        prompt["prompt"]!.GetValue<string>(),
        Enum.GetValues<PromptKind>().Single(kind => Name(kind) == prompt["kind"]!.GetValue<string>()),
        prompt["should_trigger"]!.GetValueKind() == JsonValueKind.True,
        prompt["rival"]?.GetValue<string>());

    private static bool? Expected(PromptKind kind) => kind switch
    {
        PromptKind.Positive or PromptKind.Paraphrased => true,
        PromptKind.Negative or PromptKind.Adversarial => false,
        _ => null,
    };
}
