using Axiomarium.Cli;
using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class DoctorCommandTests
{
    private const string Healthy = """
        AXM DOCTOR // 1 asset

          AGENTS
          01  determinism-auditor   experimental  0.1.0   OK

        1 asset · 0 errors

        """;

    private const string Broken = """
        AXM DOCTOR // 1 asset

          AGENTS
          01  determinism-auditor   ERROR

        ERROR  agents/determinism-auditor/asset.yaml:5
               Unknown maturity: "production-ready"
               Allowed: experimental, incubating, tested, stable, battle-tested

        1 asset · 1 error

        """;

    private static TempVault HealthyVault() =>
        new TempVault().Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid);

    private static TempVault BrokenVault() =>
        new TempVault().Write(
            "agents/determinism-auditor/asset.yaml",
            SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready"));

    [Fact]
    public void Healthy_vault_prints_the_asset_and_exits_0()
    {
        using var vault = HealthyVault();

        var (exitCode, output, error) = CliRun.Run(["doctor", "--root", vault.Root]);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(Healthy, output);
        Assert.Empty(error);
    }

    [Fact]
    public void Broken_manifest_prints_the_error_and_exits_1()
    {
        using var vault = BrokenVault();

        var (exitCode, output, _) = CliRun.Run(["doctor", "--root", vault.Root]);

        Assert.Equal(AxmCli.ErrorsFound, exitCode);
        Assert.Equal(Broken, output);
    }

    [Fact]
    public void Kinds_get_their_own_blocks_and_indices_run_across_them()
    {
        using var vault = new TempVault()
            .Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid)
            .Write("hooks/scope-sheriff/asset.yaml", TempVault.Manifest("hook", "scope-sheriff"));

        var (_, output, _) = CliRun.Run(["doctor", "--root", vault.Root]);

        Assert.Equal(
            """
            AXM DOCTOR // 2 assets

              AGENTS
              01  determinism-auditor   experimental  0.1.0   OK

              HOOKS
              02  scope-sheriff         experimental  0.1.0   OK

            2 assets · 0 errors

            """,
            output);
    }

    [Fact]
    public void No_vault_exits_2_on_stderr()
    {
        using var vault = new TempVault().Folder("docs");

        var (exitCode, output, error) = CliRun.Run(["doctor", "--root", vault.Root]);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Empty(output);
        Assert.StartsWith($"axm: No vault found in {vault.Root}.", error);
        Assert.EndsWith("\n     Run axm doctor inside a vault, or pass --root <dir>.\n", error);
    }

    [Theory]
    [InlineData("doctor", "--bogus")]
    [InlineData("frobnicate")]
    public void Bad_arguments_exit_2(params string[] args)
    {
        var (exitCode, _, error) = CliRun.Run(args);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.StartsWith("axm: ", error);
    }

    [Fact]
    public void Terminal_output_is_plain_output_plus_color_and_kaomoji()
    {
        using var healthy = HealthyVault();
        using var broken = BrokenVault();

        foreach (var (vault, face) in new[] { (healthy, Kaomoji.AllClear), (broken, Kaomoji.SomeErrors) })
        {
            var (_, plain, _) = CliRun.Run(["doctor", "--root", vault.Root]);
            var (_, fancy, _) = CliRun.Run(["doctor", "--root", vault.Root], terminal: true);

            Assert.Contains("\u001b[", fancy);
            Assert.Contains(face, fancy);
            Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {face}", ""));
        }
    }

    [Fact]
    public void Argument_errors_in_a_terminal_get_color_and_kaomoji()
    {
        var (_, _, plain) = CliRun.Run(["doctor", "--bogus"]);
        var (exitCode, _, fancy) = CliRun.Run(["doctor", "--bogus"], terminal: true);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.EndsWith("\n     Run axm --help for usage.\n", plain);
        Assert.Contains("\u001b[", fancy);
        Assert.Contains(Kaomoji.CouldNotRun, fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.CouldNotRun}", ""));
    }

    [Fact]
    public void Couldnt_run_in_a_terminal_gets_its_kaomoji_on_stderr()
    {
        using var vault = new TempVault().Folder("docs");

        var (_, _, plain) = CliRun.Run(["doctor", "--root", vault.Root]);
        var (_, _, fancy) = CliRun.Run(["doctor", "--root", vault.Root], terminal: true);

        Assert.Contains(Kaomoji.CouldNotRun, fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.CouldNotRun}", ""));
    }

    [Theory]
    [InlineData(0, 0, Kaomoji.AllClear)]
    [InlineData(0, 2, Kaomoji.WarningsOnly)]
    [InlineData(1, 0, Kaomoji.SomeErrors)]
    [InlineData(2, 3, Kaomoji.SomeErrors)]
    [InlineData(3, 0, Kaomoji.ManyErrors)]
    public void Kaomoji_matches_the_outcome(int errors, int warnings, string expected)
    {
        Assert.Equal(expected, Kaomoji.ForOutcome(errors, warnings));
    }

    [Fact]
    public void Version_in_a_terminal_has_its_kaomoji()
    {
        var (exitCode, output, _) = CliRun.Run(["--version"], terminal: true);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal($"0.1.0-dev  {Kaomoji.Version}\n", CliRun.StripColor(output));
    }
}
