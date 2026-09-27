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

    /// <summary>Creates an empty folder.</summary>
    public TempVault Folder(string relativePath)
    {
        Directory.CreateDirectory(Full(relativePath));
        return this;
    }

    /// <summary>A valid manifest for an asset of the given kind and name.</summary>
    public static string Manifest(string kind, string name) =>
        SampleManifests.Valid.Replace("name: determinism-auditor", $"name: {name}").Replace("kind: agent", $"kind: {kind}");

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
