using Axiomarium.Cli;
using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class DoctorCommandTests
{
    private const string Healthy = """
        AXM DOCTOR // 1 asset · 0 instruction files

          AGENTS
          01  determinism-auditor   experimental  0.1.0   OK

        1 asset · 0 instruction files · 0 errors

        """;

    private const string Broken = """
        AXM DOCTOR // 1 asset · 0 instruction files

          AGENTS
          01  determinism-auditor   ERROR

        ERROR  agents/determinism-auditor/asset.yaml:5
               Unknown maturity: "production-ready"
               Allowed: experimental, incubating, tested, stable, battle-tested

        1 asset · 0 instruction files · 1 error

        """;

    private static (int ExitCode, string Output, string Error) Doctor(TempVault vault, bool terminal = false, string? root = null) =>
        CliRun.Run(["doctor", "--root", root ?? vault.Root], terminal: terminal, machine: TestMachine.For(vault.Root));

    private static string Missing(TempVault vault) => Path.Combine(vault.Root, "nowhere");

    private static TempVault HealthyVault() =>
        new TempVault().Asset("agents/determinism-auditor", SampleManifests.Valid);

    private static TempVault BrokenVault() =>
        new TempVault().Asset(
            "agents/determinism-auditor",
            SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready"));

    [Fact]
    public void Healthy_vault_prints_the_asset_and_exits_0()
    {
        using var vault = HealthyVault();

        var (exitCode, output, error) = Doctor(vault);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal(Healthy, output);
        Assert.Empty(error);
    }

    [Fact]
    public void Broken_manifest_prints_the_error_and_exits_1()
    {
        using var vault = BrokenVault();

        var (exitCode, output, _) = Doctor(vault);

        Assert.Equal(AxmCli.ErrorsFound, exitCode);
        Assert.Equal(Broken, output);
    }

    [Fact]
    public void Kinds_get_their_own_blocks_and_indices_run_across_them()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Asset("hooks/scope-sheriff", TempVault.Manifest("hook", "scope-sheriff"));

        var (_, output, _) = Doctor(vault);

        Assert.Equal(
            """
            AXM DOCTOR // 2 assets · 0 instruction files

              AGENTS
              01  determinism-auditor   experimental  0.1.0   OK

              HOOKS
              02  scope-sheriff         experimental  0.1.0   OK

            2 assets · 0 instruction files · 0 errors

            """,
            output);
    }

    [Fact]
    public void Any_repo_gets_its_instruction_files_and_findings_without_failing()
    {
        using var vault = new TempVault()
            .Folder(".git")
            .Write("CLAUDE.md", "See @docs/testing.md for tests.\n")
            .Write("AGENTS.md", "agents\n")
            .Write("src/app.cs", "class App { }\n");

        var (exitCode, output, error) = Doctor(vault, root: Path.Combine(vault.Root, "src"));

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Empty(error);
        Assert.Equal(
            """
            AXM DOCTOR // 2 instruction files

              INSTRUCTIONS
              01  CLAUDE.md   claude-code
              02  AGENTS.md   codex

            WARNING  agents-md-hidden
                     Claude Code skips AGENTS.md, because CLAUDE.md exists and doesn't import it, so instructions written there for every agent never reach Claude Code.
                     Fix: Add @AGENTS.md to CLAUDE.md.

            WARNING  dead-import
                     CLAUDE.md:1 imports docs/testing.md, which does not exist, so Claude Code loads nothing in its place.
                     Fix: Restore the file, or remove the import.

            2 instruction files · 0 errors · 2 warnings

            """,
            output);
    }

    [Fact]
    public void Ignored_files_are_counted_and_a_broken_config_is_an_error()
    {
        using var ignored = new TempVault()
            .Write("axiomarium.yaml", "doctor:\n  ignore:\n    - fixtures/\n")
            .Write("AGENTS.md", "agents\n")
            .Write("fixtures/broken/CLAUDE.md", "See @docs/missing.md\n");
        using var broken = new TempVault().Write("axiomarium.yaml", "doctor:\n  skip: []\n");

        var (_, output, _) = Doctor(ignored);
        var (exitCode, brokenOutput, _) = Doctor(broken);

        Assert.EndsWith("1 instruction file · 1 ignored · 0 errors\n", output);
        Assert.Equal(AxmCli.ErrorsFound, exitCode);
        Assert.Contains("ERROR  axiomarium.yaml:2\n       Unknown field: doctor.skip\n", brokenOutput);
    }

    [Fact]
    public void A_missing_folder_exits_2_on_stderr()
    {
        using var vault = new TempVault();

        var (exitCode, output, error) = Doctor(vault, root: Missing(vault));

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.Empty(output);
        Assert.StartsWith($"axm: The folder {Missing(vault)} does not exist.", error);
    }

    [Theory]
    [InlineData("doctor", "--bogus")]
    [InlineData("frobnicate")]
    public void Bad_arguments_exit_2(params string[] args)
    {
        var (exitCode, _, error) = CliRun.Run(args, machine: TestMachine.For(Path.GetTempPath()));

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
            var (_, plain, _) = Doctor(vault);
            var (_, fancy, _) = Doctor(vault, terminal: true);

            Assert.Contains("\u001b[", fancy);
            Assert.Contains(face, fancy);
            Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {face}", ""));
        }
    }

    [Fact]
    public void Argument_errors_in_a_terminal_get_color_and_kaomoji()
    {
        var machine = TestMachine.For(Path.GetTempPath());
        var (_, _, plain) = CliRun.Run(["doctor", "--bogus"], machine: machine);
        var (exitCode, _, fancy) = CliRun.Run(["doctor", "--bogus"], terminal: true, machine: machine);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.EndsWith("\n     Run axm --help for usage.\n", plain);
        Assert.Contains("\u001b[", fancy);
        Assert.Contains(Kaomoji.CouldNotRun, fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.CouldNotRun}", ""));
    }

    [Fact]
    public void Couldnt_run_in_a_terminal_gets_its_kaomoji_on_stderr()
    {
        using var vault = new TempVault();

        var (_, _, plain) = Doctor(vault, root: Missing(vault));
        var (_, _, fancy) = Doctor(vault, terminal: true, root: Missing(vault));

        Assert.Contains(Kaomoji.CouldNotRun, fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.CouldNotRun}", ""));
    }

    // With stdout redirected to a file, only stderr is a console, and only its mode decides its color.
    [Fact]
    public void Stderr_color_follows_stderrs_own_terminal()
    {
        using var vault = new TempVault();

        var (_, _, error) = CliRun.Run(["doctor", "--root", Missing(vault)], terminal: true, virtualTerminal: false, errorVirtualTerminal: true, machine: TestMachine.For(vault.Root));

        Assert.Contains("\u001b[", error);
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
        Assert.Equal($"{RepoRoot.Version}  {Kaomoji.Version}\n", CliRun.StripColor(output));
    }
}
