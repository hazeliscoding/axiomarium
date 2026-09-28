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

    private static Task<(int ExitCode, string Output)> RunAsync(params string[] args) => RunWithInputAsync(null, args);

    private static async Task<(int ExitCode, string Output)> RunWithInputAsync(string? stdin, params string[] args)
    {
        var start = new ProcessStartInfo(Binary)
        {
            RedirectStandardInput = stdin is not null,
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
        if (stdin is not null)
        {
            // Raw UTF-8 bytes, as Claude Code writes them, whatever this process's console code page is.
            await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(stdin), TestContext.Current.CancellationToken);
            process.StandardInput.Close();
        }

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
        Assert.Equal(RepoRoot.Version, output.Trim());
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
        using var vault = new TempVault().Asset(
            "agents/determinism-auditor",
            SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready"));

        var (exitCode, output) = await RunAsync("doctor", "--root", vault.Root);

        Assert.Equal(1, exitCode);
        Assert.Contains("agents/determinism-auditor/asset.yaml:5", output);
        Assert.Contains("Unknown maturity: \"production-ready\"", output);
    }

    [Fact(Skip = NoBinary, SkipUnless = nameof(HasBinary))]
    public async Task Scope_sheriff_reads_utf8_json_on_stdin()
    {
        using var repo = new TempVault().Write(".axm/scope", "src/api/**\n");
        var fixture = Path.Combine(RepoRoot.Path, "tests", "Axiomarium.Tests", "Fixtures", "ClaudeCode", "post-tool-use-edit.json");
        var payload = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fixture))!.AsObject();
        payload["cwd"] = repo.Root;
        payload["tool_input"]!["file_path"] = Path.Combine(repo.Root, "src", "billing", "résumé.txt");

        var (exitCode, output) = await RunWithInputAsync(payload.ToJsonString(), "hook", "scope-sheriff");

        Assert.Equal(0, exitCode);
        var context = System.Text.Json.Nodes.JsonNode.Parse(output)!["hookSpecificOutput"]!["additionalContext"]!.GetValue<string>();
        Assert.StartsWith("src/billing/résumé.txt is outside this task's scope", context);
    }
}
