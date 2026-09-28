using Axiomarium.Cli;
using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class ListCommandTests
{
    // The sample manifest supports claude-code and generic; this one supports Codex instead.
    private static readonly string CodexSkill = TempVault.Manifest("skill", "dotnet").Replace("generic: full", "codex: partial");

    private static TempVault SampleVault() =>
        new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Asset("skills/dotnet", CodexSkill)
            .Asset("hooks/scope-sheriff", TempVault.Manifest("hook", "scope-sheriff"));

    [Fact]
    public void Lists_assets_by_kind_with_maturity_version_and_harnesses()
    {
        using var vault = SampleVault();

        var (exitCode, output, error) = CliRun.Run(["list", "--root", vault.Root]);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            """
            AXM LIST // 3 assets

              AGENTS
              01  determinism-auditor   experimental  0.1.0   claude-code generic

              SKILLS
              02  dotnet                experimental  0.1.0   claude-code codex

              HOOKS
              03  scope-sheriff         experimental  0.1.0   claude-code generic

            3 assets

            """,
            output);
    }

    [Fact]
    public void Harness_filter_is_the_compatibility_table()
    {
        using var vault = SampleVault();

        var (_, output, _) = CliRun.Run(["list", "--root", vault.Root, "--harness", "codex"]);

        Assert.Equal(
            """
            AXM LIST // codex // 1 asset

              SKILLS
              01  dotnet   experimental  0.1.0   codex: partial

            1 asset

            """,
            output);
    }

    [Fact]
    public void Kind_filter_lists_one_kind()
    {
        using var vault = SampleVault();

        var (_, output, _) = CliRun.Run(["list", "--root", vault.Root, "--kind", "hook"]);

        Assert.StartsWith("AXM LIST // hooks // 1 asset\n\n  HOOKS\n  01  scope-sheriff   ", output);
    }

    [Fact]
    public void Nothing_to_list_says_so()
    {
        using var vault = SampleVault();

        var (exitCode, output, _) = CliRun.Run(["list", "--root", vault.Root, "--kind", "workflow"]);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal("AXM LIST // workflows // 0 assets\n\n0 assets\n", output);
    }

    [Fact]
    public void An_invalid_asset_is_listed_as_invalid_and_list_still_passes()
    {
        using var vault = SampleVault().Asset(
            "agents/other",
            TempVault.Manifest("agent", "other").Replace("maturity: experimental", "maturity: production-ready"));

        var (exitCode, output, _) = CliRun.Run(["list", "--root", vault.Root, "--harness", "codex"]);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(
            """
            AXM LIST // codex // 2 assets

              AGENTS
              01  other    INVALID

              SKILLS
              02  dotnet   experimental  0.1.0   codex: partial

            2 assets · 1 invalid
            Run axm validate to see why.

            """,
            output);
    }

    [Theory]
    [InlineData("--harness", "vim")]
    [InlineData("--kind", "agents")]
    public void Unknown_filter_values_are_usage_errors(string option, string value)
    {
        using var vault = SampleVault();

        var (exitCode, _, error) = CliRun.Run(["list", "--root", vault.Root, option, value]);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Contains(value, error);
    }

    [Fact]
    public void Terminal_output_is_plain_output_plus_color_and_kaomoji()
    {
        using var vault = SampleVault().Asset("agents/other", "name: other\n");

        var (_, plain, _) = CliRun.Run(["list", "--root", vault.Root]);
        var (_, fancy, _) = CliRun.Run(["list", "--root", vault.Root], terminal: true);

        Assert.Contains("\u001b[", fancy);
        Assert.Contains(Kaomoji.SomeErrors, fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.SomeErrors}", ""));
    }
}
