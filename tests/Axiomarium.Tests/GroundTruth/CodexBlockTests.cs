using System.Text.Json.Nodes;
using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class CodexBlockTests
{
    private static readonly Dictionary<string, string> Files = new()
    {
        ["home/.codex/AGENTS.md"] = "# Global\nMARKER home/.codex/AGENTS.md\n",
        ["repo/AGENTS.md"] = "# Root\nMARKER repo/AGENTS.md\n",
        ["repo/src/AGENTS.md"] = "# Src\nMARKER repo/src/AGENTS.md\nmore text here\n",
    };

    // What `codex debug prompt-input` prints: a list of messages, one of them the AGENTS.md block.
    private static string PromptInput(string? instructions)
    {
        var messages = new JsonArray { Message("developer", "You are Codex.") };
        if (instructions is not null)
        {
            messages.Add(Message("user", $"# AGENTS.md instructions for C:\\repo\n\n<INSTRUCTIONS>\n{instructions}\n</INSTRUCTIONS>"));
        }

        messages.Add(Message("user", "<environment_context>…</environment_context>"));
        return messages.ToJsonString();
    }

    private static JsonObject Message(string role, string text) => new()
    {
        ["type"] = "message",
        ["role"] = role,
        ["content"] = new JsonArray { new JsonObject { ["type"] = "input_text", ["text"] = text } },
    };

    [Fact]
    public void Global_then_project_files_in_order()
    {
        var block = "# Global\nMARKER home/.codex/AGENTS.md" + "\n\n--- project-doc ---\n\n" + Files["repo/AGENTS.md"] + "\n\n" + Files["repo/src/AGENTS.md"];

        var loaded = CodexBlock.Parse(PromptInput(block), Files);

        Assert.Equal(
            [
                new LoadedFile("home/.codex/AGENTS.md", 37),
                new LoadedFile("repo/AGENTS.md", 29),
                new LoadedFile("repo/src/AGENTS.md", 47),
            ],
            loaded);
    }

    [Fact]
    public void A_file_cut_by_the_budget_is_marked_with_the_bytes_that_made_it()
    {
        // Cut after the file's marker line, which is why markers go on the first line.
        var block = Files["repo/AGENTS.md"] + "\n\n" + Files["repo/src/AGENTS.md"][..35];

        var loaded = CodexBlock.Parse(PromptInput(block), Files);

        Assert.Equal([new LoadedFile("repo/AGENTS.md", 29), new LoadedFile("repo/src/AGENTS.md", 35, Cut: true)], loaded);
    }

    [Fact]
    public void A_file_cut_before_its_marker_is_an_error()
    {
        var block = Files["repo/AGENTS.md"] + "\n\n" + Files["repo/src/AGENTS.md"][..20];

        Assert.Throws<GroundTruthException>(() => CodexBlock.Parse(PromptInput(block), Files));
    }

    [Fact]
    public void No_block_means_nothing_loaded()
    {
        Assert.Empty(CodexBlock.Parse(PromptInput(null), Files));
    }

    [Fact]
    public void Text_from_outside_the_scenario_is_an_error()
    {
        var block = "# Someone's real global file\n" + "\n\n--- project-doc ---\n\n" + Files["repo/AGENTS.md"];

        var problem = Assert.Throws<GroundTruthException>(() => CodexBlock.Parse(PromptInput(block), Files));

        Assert.StartsWith("Codex loaded text the scenario can't account for", problem.Message);
    }
}
