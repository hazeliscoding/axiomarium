using Axiomarium.Cli;
using Axiomarium.Cli.Output;

namespace Axiomarium.Tests.Cli;

public class ValidateCommandTests
{
    private static TempVault HealthyVault() => new TempVault().Asset("agents/determinism-auditor", SampleManifests.Valid);

    private static TempVault BrokenVault() =>
        new TempVault().Asset(
            "agents/determinism-auditor",
            SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready"));

    [Fact]
    public void Healthy_vault_prints_only_the_summary_and_exits_0()
    {
        using var vault = HealthyVault();

        var (exitCode, output, error) = CliRun.Run(["validate", "--root", vault.Root]);

        Assert.Equal(AxmCli.Passed, exitCode);
        Assert.Equal("AXM VALIDATE // 1 asset\n\n1 asset · 0 errors\n", output);
        Assert.Empty(error);
    }

    [Fact]
    public void Problems_print_without_the_inventory_and_exit_1()
    {
        using var vault = BrokenVault();

        var (exitCode, output, _) = CliRun.Run(["validate", "--root", vault.Root]);

        Assert.Equal(AxmCli.ErrorsFound, exitCode);
        Assert.Equal(
            """
            AXM VALIDATE // 1 asset

            ERROR  agents/determinism-auditor/asset.yaml:5
                   Unknown maturity: "production-ready"
                   Allowed: experimental, incubating, tested, stable, battle-tested

            1 asset · 1 error

            """,
            output);
    }

    [Fact]
    public void Validate_prints_the_problems_and_the_outcome_doctor_prints()
    {
        using var healthy = HealthyVault();
        using var broken = BrokenVault();

        foreach (var vault in new[] { healthy, broken })
        {
            var (_, validate, _) = CliRun.Run(["validate", "--root", vault.Root]);
            var (_, doctor, _) = CliRun.Run(["doctor", "--root", vault.Root], machine: TestMachine.For(vault.Root));

            var problems = validate[(validate.IndexOf("\n\n", StringComparison.Ordinal) + 2)..validate.LastIndexOf("1 asset", StringComparison.Ordinal)];
            Assert.Contains(problems, doctor);
            Assert.EndsWith(validate[validate.LastIndexOf(" · ", StringComparison.Ordinal)..], doctor);
        }
    }

    [Fact]
    public void No_vault_hint_names_the_command()
    {
        using var vault = new TempVault().Folder("docs");

        var (exitCode, _, error) = CliRun.Run(["validate", "--root", vault.Root]);

        Assert.Equal(AxmCli.CouldNotRun, exitCode);
        Assert.EndsWith("\n     Run axm validate inside a vault, or pass --root <dir>.\n", error);
    }

    [Fact]
    public void Terminal_output_is_plain_output_plus_color_and_kaomoji()
    {
        using var vault = BrokenVault();

        var (_, plain, _) = CliRun.Run(["validate", "--root", vault.Root]);
        var (_, fancy, _) = CliRun.Run(["validate", "--root", vault.Root], terminal: true);

        Assert.Contains("\u001b[", fancy);
        Assert.Equal(plain, CliRun.StripColor(fancy).Replace($"  {Kaomoji.SomeErrors}", ""));
    }
}
