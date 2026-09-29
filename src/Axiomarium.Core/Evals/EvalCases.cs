using System.Text.Json;
using System.Text.Json.Nodes;
using Axiomarium.Core.Health;
using Axiomarium.Core.Manifests;
using Axiomarium.Core.Paths;
using Axiomarium.Core.Schemas;

namespace Axiomarium.Core.Evals;

/// <summary>Which kind of eval a case is, which decides its folder under the asset's <c>evals/</c>.</summary>
public enum EvalType
{
    /// <summary>Tests what the asset does once it's active. Kept in <c>evals/behavioral/</c>.</summary>
    Behavioral,

    /// <summary>Guards against a failure that happened, and names it. Kept in <c>evals/regression/</c>.</summary>
    Regression,
}

/// <summary>What a check looks at.</summary>
public enum CheckKind
{
    /// <summary>The files in the copy after the session: whether one matches a glob, and what it contains.</summary>
    File,

    /// <summary>A command run in the copy after the session, and its exit code.</summary>
    Run,

    /// <summary>The skills the session loaded and the agents it ran, as the harness recorded them.</summary>
    Loaded,

    /// <summary>The commands the session ran.</summary>
    Ran,

    /// <summary>The session's final message.</summary>
    Reply,
}

/// <summary>One check of an eval case. A run passes when every check passes.</summary>
/// <param name="Kind">What the check looks at.</param>
/// <param name="Target">
/// A glob relative to the copy for <see cref="CheckKind.File"/>, a command for <see cref="CheckKind.Run"/>, a
/// skill or agent name for <see cref="CheckKind.Loaded"/>, a command's leading words for <see cref="CheckKind.Ran"/>,
/// and text for <see cref="CheckKind.Reply"/>.
/// </param>
/// <param name="Not">Whether the check passes when it would fail otherwise. Never set for <see cref="CheckKind.Run"/>.</param>
/// <param name="Contains">For <see cref="CheckKind.File"/> only: text a matching file must contain, in any case.</param>
/// <param name="Exit">For <see cref="CheckKind.Run"/> only: the exit code to expect.</param>
public sealed record EvalCheck(CheckKind Kind, string Target, bool Not = false, string? Contains = null, int Exit = 0);

/// <summary>An eval case: a prompt run in a copy of the case's repo with the asset installed, and its checks.</summary>
/// <param name="Name">The case's folder name, in kebab-case.</param>
/// <param name="Type">Whether it's a behavioral or a regression case.</param>
/// <param name="Prompt">The session's first message.</param>
/// <param name="Allow">The commands the session may run, each the start of a command line. Often empty.</param>
/// <param name="Checks">The checks, in file order. Never empty.</param>
/// <param name="Rubric">What a model grades each run against, or <see langword="null"/> when the case has no judge.</param>
/// <param name="Guards">For a regression case, the failure it guards against. <see langword="null"/> for a behavioral one.</param>
public sealed record EvalCase(
    string Name,
    EvalType Type,
    string Prompt,
    IReadOnlyList<string> Allow,
    IReadOnlyList<EvalCheck> Checks,
    string? Rubric,
    string? Guards);

/// <summary>A problem in an eval case's <c>eval.yaml</c>.</summary>
/// <param name="Location">Where in the file, when known.</param>
/// <param name="Message">What is wrong, as one line.</param>
/// <param name="Detail">Lines that help fix it. Often empty.</param>
public sealed record CaseProblem(SourceLocation? Location, string Message, IReadOnlyList<string> Detail);

/// <summary>An <c>eval.yaml</c> as read: the case when the file is valid, and every problem found.</summary>
/// <param name="Case">The case, or <see langword="null"/> when there is any problem.</param>
/// <param name="Problems">Every problem, in file order. Empty when the file is valid.</param>
public sealed record EvalCaseRead(EvalCase? Case, IReadOnlyList<CaseProblem> Problems);

/// <summary>Reads and checks an asset's eval cases, the <c>evals/behavioral/&lt;case&gt;/eval.yaml</c> and <c>evals/regression/&lt;case&gt;/eval.yaml</c> files <c>axm eval</c> runs.</summary>
public static class EvalCases
{
    /// <summary>The file in a case's folder that describes the case.</summary>
    public const string FileName = "eval.yaml";

    private static readonly (string Key, CheckKind Kind)[] Kinds =
        [("file", CheckKind.File), ("run", CheckKind.Run), ("loaded", CheckKind.Loaded), ("ran", CheckKind.Ran), ("reply", CheckKind.Reply)];

    /// <summary>The folder under an asset's <c>evals/</c> that holds cases of <paramref name="type"/>, such as <c>behavioral</c>.</summary>
    /// <param name="type">The kind of eval.</param>
    /// <returns>The folder's name, which is also the name of the <c>evals</c> flag in <c>asset.yaml</c>.</returns>
    public static string Folder(EvalType type) => type == EvalType.Behavioral ? "behavioral" : "regression";

    /// <summary>
    /// Reads <paramref name="text"/> as an eval case and checks it: against its schema, then that each check is
    /// exactly one kind and takes only its own fields, that each file check is a valid glob, and that a regression
    /// case, and only a regression case, names the failure it guards.
    /// </summary>
    /// <param name="text">The contents of <c>eval.yaml</c>.</param>
    /// <param name="name">The case's folder name, which becomes its name.</param>
    /// <param name="type">The kind of eval, from the folder the case is in.</param>
    /// <returns>The case when the file is valid, and every problem otherwise.</returns>
    public static EvalCaseRead Read(string text, string name, EvalType type)
    {
        var parsed = YamlDocument.Parse(text);
        return parsed.Problem is { } yaml
            ? new EvalCaseRead(null, [new CaseProblem(yaml.Location, yaml.Message, [])])
            : Check(parsed.Root, parsed.Locations, name, type);
    }

    private static EvalCaseRead Check(JsonNode? document, IReadOnlyDictionary<string, SourceLocation> locations, string name, EvalType type)
    {
        var problems = SchemaValidator.Validate(document, SchemaCatalog.Eval)
            .Select(error => new CaseProblem(Doctor.Locate(locations, error.Path), error.Message, error.Detail))
            .ToList();
        if (problems.Count > 0)
        {
            return new EvalCaseRead(null, problems);
        }

        void Problem(string path, string message) => problems.Add(new CaseProblem(Doctor.Locate(locations, path), message, []));

        var root = document!.AsObject();
        var items = root["checks"]!.AsArray();
        var checks = new List<EvalCheck>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i]!.AsObject();
            var at = $"checks[{i}]";
            var kinds = Kinds.Where(kind => item.ContainsKey(kind.Key)).ToList();
            if (kinds.Count != 1)
            {
                Problem(at, kinds.Count == 0
                    ? $"{at} needs one of file, run, loaded, ran or reply"
                    : $"{at} has both {kinds[0].Key} and {kinds[1].Key}, and a check is one of them");
                continue;
            }

            var (key, kind) = kinds[0];
            var target = item[key]!.GetValue<string>();
            if (item.ContainsKey("contains") && kind != CheckKind.File)
            {
                Problem($"{at}.contains", $"{at} has contains, which only a file check takes");
            }

            if (item.ContainsKey("exit") && kind != CheckKind.Run)
            {
                Problem($"{at}.exit", $"{at} has exit, which only a run check takes");
            }

            var exit = 0;
            if (item["exit"] is JsonValue code && !code.TryGetValue(out exit))
            {
                Problem($"{at}.exit", $"{at}.exit is too large for an exit code");
            }

            if (item.ContainsKey("not") && kind == CheckKind.Run)
            {
                Problem($"{at}.not", $"{at} is a run check, which says what to expect with exit, not with not");
            }

            if (kind == CheckKind.File && !Glob.TryParse(target, out _, out var glob))
            {
                Problem($"{at}.file", $"{at}.file isn't a valid glob: {glob}");
            }

            checks.Add(new EvalCheck(
                kind,
                target,
                item["not"]?.GetValueKind() == JsonValueKind.True,
                item["contains"]?.GetValue<string>(),
                exit));
        }

        var guards = root["guards"]?.GetValue<string>();
        if (type == EvalType.Regression && guards is null)
        {
            Problem("", "A regression case needs guards: the failure it guards against");
        }
        else if (type == EvalType.Behavioral && guards is not null)
        {
            Problem("guards", "guards is only for regression cases");
        }

        if (problems.Count > 0)
        {
            return new EvalCaseRead(null, [.. problems.OrderBy(problem => problem.Location?.Line ?? 0)]);
        }

        return new EvalCaseRead(
            new EvalCase(
                name,
                type,
                root["prompt"]!.GetValue<string>(),
                root["allow"] is JsonArray allow ? [.. allow.Select(command => command!.GetValue<string>())] : [],
                checks,
                root["judge"]?["rubric"]?.GetValue<string>(),
                guards),
            []);
    }
}
