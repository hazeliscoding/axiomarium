namespace Axiomarium.Tests;

/// <summary>A throwaway vault on disk. Deleted on dispose.</summary>
internal sealed class TempVault : IDisposable
{
    /// <summary>The vault's root directory.</summary>
    public string Root { get; } = Directory.CreateTempSubdirectory("axm-").FullName;

    /// <summary>Writes a file, creating its folders. <paramref name="relativePath"/> uses forward slashes.</summary>
    public TempVault Write(string relativePath, string text)
    {
        var full = Full(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return this;
    }

    /// <summary>Writes an asset: <c>asset.yaml</c> and the content file its kind folder calls for, such as <c>agent.md</c>.</summary>
    public TempVault Asset(string folder, string manifest) =>
        Write($"{folder}/asset.yaml", manifest).Write($"{folder}/{ContentFiles[folder[..folder.IndexOf('/')]]}", $"# {folder}\n");

    // Spelled out rather than taken from the core, so a wrong name in the core fails a test.
    private static readonly Dictionary<string, string> ContentFiles = new()
    {
        ["agents"] = "agent.md",
        ["skills"] = "skill.md",
        ["hooks"] = "hook.md",
        ["policies"] = "policy.md",
        ["workflows"] = "workflow.md",
        ["experiments"] = "experiment.md",
    };

    /// <summary>Creates an empty folder.</summary>
    public TempVault Folder(string relativePath)
    {
        Directory.CreateDirectory(Full(relativePath));
        return this;
    }

    /// <summary>A valid manifest for an asset of the given kind and name, with its kind's block when it has one.</summary>
    public static string Manifest(string kind, string name) =>
        SampleManifests.Valid.Replace("name: determinism-auditor", $"name: {name}").Replace("kind: agent", $"kind: {kind}")
        + SampleManifests.Blocks.GetValueOrDefault(kind, "");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder isn't worth failing a test over.
        }
    }

    private string Full(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
