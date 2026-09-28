namespace Axiomarium.Tests;

/// <summary>The repository checkout the tests run from.</summary>
internal static class RepoRoot
{
    /// <summary>The directory that holds <c>axiomarium.slnx</c>, found by walking up from the test binary.</summary>
    public static string Path { get; } = Find();

    /// <summary>The version in <c>Directory.Build.props</c>, which the binary reports and each release is tagged with.</summary>
    public static string Version { get; } =
        System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(Path, "Directory.Build.props")).Descendants("Version").Single().Value;

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "axiomarium.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No axiomarium.slnx above the test binary.");
    }
}
