using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class ClaudeTranscriptTests
{
    private static readonly string Run = Path.Combine(Path.GetTempPath(), "axm-ground-truth", "basic");

    private static string At(string relative) => Path.Combine(Run, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Attachment(JsonObject attachment) => new JsonObject { ["type"] = "attachment", ["attachment"] = attachment }.ToJsonString();

    private static JsonObject File(string relative, string scope, string content) =>
        new() { ["path"] = At(relative), ["type"] = scope, ["content"] = content };

    private static string Hook(string relative, string reason) =>
        new JsonObject { ["file_path"] = At(relative), ["load_reason"] = reason, ["memory_type"] = "Project" }.ToJsonString();

    private static string[] Transcript() =>
    [
        new JsonObject { ["type"] = "user" }.ToJsonString(),
        Attachment(new JsonObject
        {
            ["type"] = "instructions",
            ["files"] = new JsonArray
            {
                File("home/.claude/CLAUDE.md", "User", "# User\nMARKER home/.claude/CLAUDE.md"),
                File("repo/CLAUDE.md", "Project", "# Root\nMARKER repo/CLAUDE.md\n\n@docs/extra.md"),
                File("repo/docs/extra.md", "Project", "# Extra\nMARKER repo/docs/extra.md"),
            },
        }),
        Attachment(new JsonObject { ["type"] = "skill_listing" }),
        Attachment(new JsonObject
        {
            ["type"] = "nested_memory",
            ["path"] = At("repo/src/CLAUDE.md"),
            ["content"] = File("repo/src/CLAUDE.md", "Project", "# Src\nMARKER repo/src/CLAUDE.md\n"),
        }),
        Attachment(new JsonObject
        {
            ["type"] = "hook_additional_context",
            ["content"] = new JsonArray { $"Contents of {At("repo/src/api/AGENTS.md")}:\n\n# Api\nMARKER repo/src/api/AGENTS.md\n" },
            ["hookEvent"] = "PostToolUse",
        }),
    ];

    private static readonly string[] HookLog =
    [
        Hook("repo/CLAUDE.md", "session_start"),
        Hook("repo/docs/extra.md", "include"),
        Hook("home/.claude/CLAUDE.md", "session_start"),
        Hook("repo/src/CLAUDE.md", "nested_traversal"),
    ];

    [Fact]
    public void Launch_files_keep_the_transcript_order_and_take_reasons_from_the_hook()
    {
        var (launch, _) = ClaudeTranscript.Parse(Transcript(), HookLog, Run);

        Assert.Equal(
            [
                new LoadedFile("home/.claude/CLAUDE.md", 36, "User", "session_start"),
                new LoadedFile("repo/CLAUDE.md", 44, "Project", "session_start"),
                new LoadedFile("repo/docs/extra.md", 33, "Project", "include"),
            ],
            launch);
    }

    [Fact]
    public void Files_loaded_on_read_include_nested_agents_md_from_the_builtin_plugin()
    {
        var (_, read) = ClaudeTranscript.Parse(Transcript(), HookLog, Run);

        Assert.Equal(
            [
                new LoadedFile("repo/src/CLAUDE.md", 32, "Project", "nested_traversal"),
                new LoadedFile("repo/src/api/AGENTS.md", 36, "Project", "agents-md"),
            ],
            read);
    }

    [Fact]
    public void A_file_from_outside_the_run_is_an_error_that_names_only_its_path()
    {
        var outside = Path.Combine(Path.GetTempPath(), "somewhere-else", "CLAUDE.md");
        var transcript = new[]
        {
            Attachment(new JsonObject
            {
                ["type"] = "instructions",
                ["files"] = new JsonArray { new JsonObject { ["path"] = outside, ["type"] = "Project", ["content"] = "private text" } },
            }),
        };

        var problem = Assert.Throws<GroundTruthException>(() => ClaudeTranscript.Parse(transcript, [], Run));

        Assert.Contains(outside, problem.Message);
        Assert.DoesNotContain("private text", problem.Message);
    }

    // AGENTS.md never fires the hook, but a CLAUDE file or rule always should.
    [Fact]
    public void A_claude_file_the_hook_missed_is_an_error()
    {
        var hookLog = HookLog.Where(line => !line.Contains("extra.md", StringComparison.Ordinal)).ToArray();

        var problem = Assert.Throws<GroundTruthException>(() => ClaudeTranscript.Parse(Transcript(), hookLog, Run));

        Assert.Contains("repo/docs/extra.md", problem.Message);
    }

    [Fact]
    public void A_file_without_its_marker_is_an_error()
    {
        var transcript = new[]
        {
            Attachment(new JsonObject
            {
                ["type"] = "instructions",
                ["files"] = new JsonArray { File("repo/CLAUDE.md", "Project", "# No marker here") },
            }),
        };

        var problem = Assert.Throws<GroundTruthException>(() => ClaudeTranscript.Parse(transcript, [], Run));

        Assert.Contains("repo/CLAUDE.md", problem.Message);
    }
}
