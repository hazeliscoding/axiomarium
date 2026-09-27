using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Axiomarium.Tests.Core;

/// <summary>Finds references to console and network APIs in a compiled assembly.</summary>
internal static class CoreDependencyScanner
{
    private static readonly string[] BannedPrefixes = ["System.Net.Http.", "System.Net.Sockets.", "Spectre."];

    /// <summary>Returns the banned types the assembly references, sorted, as "Namespace.Name".</summary>
    public static IReadOnlyList<string> FindBannedReferences(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();

        var banned = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.TypeReferences)
        {
            var type = reader.GetTypeReference(handle);
            var name = $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
            if (name == "System.Console" || BannedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                banned.Add(name);
            }
        }

        return [.. banned];
    }
}
