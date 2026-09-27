namespace Axiomarium.Tests.Core;

// The positive control for CoreDependencyTests: the scanner has to find both references here,
// which proves it would catch them in Axiomarium.Core too.
internal static class BannedApiCanary
{
    public static void Touch() => Console.WriteLine(new HttpClient().BaseAddress);
}
