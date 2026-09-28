using Axiomarium.Core.Instructions;

namespace Axiomarium.Tests;

/// <summary>A machine that lives entirely in a test folder: home/, home/.codex, home/.claude and managed/.</summary>
internal static class TestMachine
{
    public static Machine For(string root) => new(
        Home: Path.Combine(root, "home"),
        CodexHome: Path.Combine(root, "home", ".codex"),
        ClaudeConfig: Path.Combine(root, "home", ".claude"),
        ClaudeManaged: Path.Combine(root, "managed"),
        FileSystemRoot: root);

    // Only the folder goes through GetRelativePath, which on Windows would strip a trailing dot from the name.
    public static string Relative(string root, string path)
    {
        var folder = Path.GetRelativePath(root, Path.GetDirectoryName(path)!).Replace('\\', '/');
        return folder == "." ? Path.GetFileName(path) : $"{folder}/{Path.GetFileName(path)}";
    }
}
