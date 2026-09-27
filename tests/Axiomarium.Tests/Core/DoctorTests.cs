using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Tests.Core;

public class DoctorTests
{
    private static DoctorReport Report(TempVault vault)
    {
        var result = Doctor.Run(vault.Root);
        Assert.Null(result.CouldNotRun);
        return result.Report!;
    }

    [Fact]
    public void A_valid_agent_has_no_diagnostics()
    {
        using var vault = new TempVault().Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid);

        var report = Report(vault);

        Assert.Empty(report.Diagnostics);
        var asset = Assert.Single(report.Assets);
        Assert.Equal(
            new DiscoveredAsset(AssetKind.Agent, "determinism-auditor", "agents/determinism-auditor", "agents/determinism-auditor/asset.yaml", "experimental", "0.1.0"),
            asset);
    }

    [Fact]
    public void Unknown_maturity_points_at_its_line()
    {
        using var vault = new TempVault().Write(
            "agents/determinism-auditor/asset.yaml",
            SampleManifests.Valid.Replace("maturity: experimental", "maturity: production-ready"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal("agents/determinism-auditor/asset.yaml", diagnostic.File);
        Assert.Equal(new SourceLocation(5, 1), diagnostic.Location);
        Assert.Equal("Unknown maturity: \"production-ready\"", diagnostic.Message);
        Assert.Equal(["Allowed: experimental, incubating, tested, stable, battle-tested"], diagnostic.Detail);
    }

    [Fact]
    public void Missing_field_points_at_the_top_of_the_file()
    {
        using var vault = new TempVault().Write(
            "agents/determinism-auditor/asset.yaml",
            SampleManifests.Valid.Replace("version: 0.1.0\n", ""));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Missing required field: version", diagnostic.Message);
        Assert.Equal(new SourceLocation(1, 1), diagnostic.Location);
    }

    [Fact]
    public void Missing_manifest_is_an_error()
    {
        using var vault = new TempVault().Folder("agents/empty");

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("agents/empty", diagnostic.File);
        Assert.Null(diagnostic.Location);
        Assert.Equal("Missing asset.yaml", diagnostic.Message);
        Assert.Empty(diagnostic.Detail);
        Assert.Null(Assert.Single(report.Assets).ManifestFile);
    }

    [Fact]
    public void Asset_yml_gets_a_rename_hint()
    {
        using var vault = new TempVault().Write("agents/determinism-auditor/asset.yml", SampleManifests.Valid);

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Missing asset.yaml", diagnostic.Message);
        Assert.Equal(["Rename asset.yml to asset.yaml."], diagnostic.Detail);
    }

    [Fact]
    public void Empty_manifest_is_an_error()
    {
        using var vault = new TempVault().Write("agents/determinism-auditor/asset.yaml", "");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("The file is empty.", diagnostic.Message);
    }

    [Fact]
    public void Name_must_match_the_folder()
    {
        using var vault = new TempVault().Write("agents/other/asset.yaml", SampleManifests.Valid);

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(new SourceLocation(1, 1), diagnostic.Location);
        Assert.Equal("name \"determinism-auditor\" doesn't match its folder \"other\"", diagnostic.Message);
    }

    [Fact]
    public void Kind_must_match_the_folder()
    {
        using var vault = new TempVault().Write("skills/determinism-auditor/asset.yaml", SampleManifests.Valid);

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal(new SourceLocation(2, 1), diagnostic.Location);
        Assert.Equal("kind \"agent\" doesn't match its folder \"skills/\"", diagnostic.Message);
        Assert.Equal(AssetKind.Skill, Assert.Single(report.Assets).Kind);
    }

    [Fact]
    public void Duplicate_key_is_reported_with_its_line()
    {
        using var vault = new TempVault().Write("agents/determinism-auditor/asset.yaml", "name: a\nname: b\n");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Duplicate key \"name\".", diagnostic.Message);
        Assert.Equal(2, diagnostic.Location!.Value.Line);
    }

    [Fact]
    public void Folder_without_a_vault_could_not_run()
    {
        using var vault = new TempVault().Folder("docs");

        var result = Doctor.Run(vault.Root);

        Assert.Null(result.Report);
        Assert.StartsWith($"No vault found in {vault.Root}.", result.CouldNotRun);
    }

    [Fact]
    public void Missing_root_could_not_run()
    {
        var missing = Path.Combine(Path.GetTempPath(), "axm-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var result = Doctor.Run(missing);

        Assert.Equal($"The folder {missing} does not exist.", result.CouldNotRun);
    }

    [Fact]
    public void Hidden_folders_and_loose_files_are_ignored()
    {
        using var vault = new TempVault()
            .Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid)
            .Write("agents/.draft/asset.yaml", "not: [valid")
            .Write("agents/README.md", "# Agents\n");

        var report = Report(vault);

        Assert.Empty(report.Diagnostics);
        Assert.Equal("determinism-auditor", Assert.Single(report.Assets).Name);
    }

    [Fact]
    public void Assets_are_ordered_by_kind_then_name()
    {
        using var vault = new TempVault()
            .Write("skills/b/asset.yaml", TempVault.Manifest("skill", "b"))
            .Write("agents/z/asset.yaml", TempVault.Manifest("agent", "z"))
            .Write("agents/a/asset.yaml", TempVault.Manifest("agent", "a"));

        var report = Report(vault);

        Assert.Empty(report.Diagnostics);
        Assert.Equal(["agents/a", "agents/z", "skills/b"], report.Assets.Select(asset => asset.Folder));
    }

    [Fact]
    public void Paths_use_forward_slashes()
    {
        using var vault = new TempVault()
            .Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid.Replace("kind: agent", "kind: nope"))
            .Folder("hooks/empty");

        var report = Report(vault);

        Assert.All(report.Diagnostics, diagnostic => Assert.DoesNotContain('\\', diagnostic.File));
        Assert.All(report.Assets, asset => Assert.DoesNotContain('\\', asset.Folder));
        Assert.Equal(2, report.ErrorCount);
    }
}
