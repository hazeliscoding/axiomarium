using System.Runtime.InteropServices;
using System.Text.Json;
using Axiomarium.Core.Instructions;
using Axiomarium.Core.Triggers;

namespace Axiomarium.Core.Evals;

/// <summary>A fake home for an eval run's sessions: what it holds, the logins it borrows, and the environment that points the harnesses at it.</summary>
/// <param name="Files">Files to write, relative to the fake home.</param>
/// <param name="Logins">Each login to copy in for the run and delete afterwards: its real path, and where it goes, relative to the fake home.</param>
/// <param name="Environment">The variables that point the harnesses and the home folder at the fake home.</param>
/// <param name="Problem">Why no sealed home can be made on this machine, or <see langword="null"/>.</param>
/// <param name="Hint">What to do about it, or <see langword="null"/>.</param>
public sealed record SealedHome(
    IReadOnlyList<WorkspaceFile> Files,
    IReadOnlyList<(string From, string To)> Logins,
    IReadOnlyDictionary<string, string> Environment,
    string? Problem,
    string? Hint);

/// <summary>
/// Plans the sealed home eval sessions run in, as the M5 spike settled (see <c>ROADMAP.md</c>): only the user's
/// logins and model choices come along, never their plugins, hooks, MCP servers, skills or instructions. Reads, never writes.
/// </summary>
public static class SealedHomes
{
    /// <summary>Plans a sealed home at <paramref name="folder"/> for sessions on <paramref name="harnesses"/>.</summary>
    /// <param name="folder">Where the fake home goes: outside the user's home, so no instructions above it load.</param>
    /// <param name="machine">Where the user's real harness folders are, which the logins and model choices come from.</param>
    /// <param name="harnesses">The harnesses the run uses. Only their logins are needed.</param>
    /// <param name="platform">The operating system, which decides where Claude Code keeps its login.</param>
    /// <returns>
    /// The plan: Claude Code's settings with the user's model and claude.ai skill sync off; Codex's config with the
    /// user's model, reasoning effort and Windows sandbox, its plugins, remote plugins and apps off, and every skill in
    /// the real <c>~/.agents/skills</c> disabled, since Codex reads that folder on Windows whatever the environment says.
    /// Or why not: a missing login, managed Claude Code skills, or Claude Code on macOS.
    /// </returns>
    public static SealedHome Plan(string folder, Machine machine, IReadOnlyCollection<Harness> harnesses, OSPlatform platform)
    {
        var files = new List<WorkspaceFile>();
        var logins = new List<(string, string)>();
        var environment = new Dictionary<string, string>
        {
            ["CLAUDE_CONFIG_DIR"] = Path.Combine(folder, ".claude"),
            ["CODEX_HOME"] = Path.Combine(folder, ".codex"),
            ["HOME"] = folder,
            ["USERPROFILE"] = folder,
        };
        SealedHome Refused(string problem, string? hint) => new([], [], environment, problem, hint);

        if (harnesses.Contains(Harness.ClaudeCode))
        {
            if (platform == OSPlatform.OSX)
            {
                return Refused("On macOS, Claude Code keeps its login in the Keychain, and axm eval can't borrow it for a sealed home yet.", null);
            }

            var managed = Path.Combine(machine.ClaudeManaged, ".claude", "skills");
            if (Directory.Exists(managed) && Directory.EnumerateFileSystemEntries(managed).Any())
            {
                return Refused($"This machine has managed Claude Code skills in {managed}, which reach every session.", "Run axm eval on a machine without them.");
            }

            var login = Path.Combine(machine.ClaudeConfig, ".credentials.json");
            if (!File.Exists(login))
            {
                return Refused($"No Claude Code login in {Shown(machine, login)}.", "Log in to Claude Code first.");
            }

            logins.Add((login, ".claude/.credentials.json"));
            var model = ClaudeModel(Path.Combine(machine.ClaudeConfig, "settings.json"));
            files.Add(new WorkspaceFile(
                ".claude/settings.json",
                $"{{{(model is null ? "" : $"\"model\":{TriggerPrompts.Quoted(model)},")}\"syncClaudeAiSkills\":false}}\n"));
        }

        if (harnesses.Contains(Harness.Codex))
        {
            var login = Path.Combine(machine.CodexHome, "auth.json");
            if (!File.Exists(login))
            {
                return Refused($"No Codex login in {Shown(machine, login)}.", "Log in to Codex first.");
            }

            logins.Add((login, ".codex/auth.json"));
            files.Add(new WorkspaceFile(".codex/config.toml", CodexConfigFor(machine)));
        }

        return new SealedHome(files, logins, environment, null, null);
    }

    private static string CodexConfigFor(Machine machine)
    {
        var config = CodexConfig.Load(machine.CodexHome);
        var blocks = new List<string>();
        var top = new List<string>();
        if (config.Model is { } model)
        {
            top.Add($"model = {TriggerPrompts.Quoted(model)}");
        }

        if (config.ReasoningEffort is { } effort)
        {
            top.Add($"model_reasoning_effort = {TriggerPrompts.Quoted(effort)}");
        }

        if (top.Count > 0)
        {
            blocks.Add(string.Join('\n', top));
        }

        blocks.Add("[features]\nplugins = false\nremote_plugin = false\napps = false");
        if (config.WindowsSandbox is { } sandbox)
        {
            blocks.Add($"[windows]\nsandbox = {TriggerPrompts.Quoted(sandbox)}");
        }

        var skills = Path.Combine(machine.Home, ".agents", "skills");
        if (Directory.Exists(skills))
        {
            blocks.AddRange(Directory.EnumerateFiles(skills, "SKILL.md", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(skill => $"[[skills.config]]\npath = {TriggerPrompts.Quoted(skill)}\nenabled = false"));
        }

        return string.Join("\n\n", blocks) + "\n";
    }

    private static string? ClaudeModel(string settings)
    {
        if (!File.Exists(settings))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settings));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String
                ? model.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A path in the home folder is shown from ~, as everywhere else in axm's output.
    private static string Shown(Machine machine, string path) =>
        Instructions.Paths.IsUnder(path, machine.Home)
            ? "~/" + Path.GetRelativePath(machine.Home, path).Replace('\\', '/')
            : path;
}
