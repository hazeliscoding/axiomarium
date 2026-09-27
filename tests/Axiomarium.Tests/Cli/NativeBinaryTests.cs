using System.Diagnostics;
using System.Text;

namespace Axiomarium.Tests.Cli;

// M0's acceptance tests run against the published NativeAOT binary, not the in-process CLI.
// CI points AXM_BINARY at the binary it just published.
public class NativeBinaryTests
{
    private const string NoBinary = "Set AXM_BINARY to a published axm binary to run this test.";

    public static bool HasBinary => Binary.Length > 0;

    private static string Binary => Environment.GetEnvironmentVariable("AXM_BINARY") ?? "";

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var start = new ProcessStartInfo(Binary)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }

    [Fact(Skip = NoBinary, SkipUnless = nameof(HasBinary))]
    public async Task Prints_its_version()
    {
        var (exitCode, output) = await RunAsync("--version");

        Assert.Equal(0, exitCode);
        Assert.Equal("0.1.0-dev", output.Trim());
    }

    [Fact(Skip = NoBinary, SkipUnless = nameof(HasBinary))]
    public async Task Doctor_passes_on_this_repo()
    {
        var (exitCode, output) = await RunAsync("doctor", "--root", RepoRoot.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("determinism-auditor", output);
        Assert.Contains(" · 0 errors", output);
    }

    [Fact(Skip = NoBinary, SkipUnless = nameof(HasBinary))]
    public async Task Doctor_names_the_field_in_a_broken_manifest()
    {
        using var vault = new TempVault().Write(
            "agents/determinism-auditor/asset.yaml",
            SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready"));

        var (exitCode, output) = await RunAsync("doctor", "--root", vault.Root);

        Assert.Equal(1, exitCode);
        Assert.Contains("agents/determinism-auditor/asset.yaml:5", output);
        Assert.Contains("Unknown maturity: \"production-ready\"", output);
    }
}
