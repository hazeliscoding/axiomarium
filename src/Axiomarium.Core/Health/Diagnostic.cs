using Axiomarium.Core.Manifests;

namespace Axiomarium.Core.Health;

/// <summary>How serious a <see cref="Diagnostic"/> is.</summary>
public enum Severity
{
    /// <summary>Something is broken: an invalid or missing manifest. <c>axm doctor</c> exits 1.</summary>
    Error,

    /// <summary>Something is likely wrong, but nothing is broken yet.</summary>
    Warning,
}

/// <summary>One problem the doctor found.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="File">The file or folder, relative to the vault root, with forward slashes.</param>
/// <param name="Location">Where in the file, when known.</param>
/// <param name="Message">What is wrong, as one line.</param>
/// <param name="Detail">Lines that help fix it, such as the allowed values. Often empty.</param>
public sealed record Diagnostic(Severity Severity, string File, SourceLocation? Location, string Message, IReadOnlyList<string> Detail);
