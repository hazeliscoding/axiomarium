using Axiomarium.Core.Assets;
using Axiomarium.Core.Health;
using Axiomarium.Core.Manifests;

namespace Axiomarium.Tests.Core;

public class DoctorTests
{
    private static DoctorReport Report(TempVault vault)
    {
        var result = Doctor.Run(vault.Root);
        Assert.Null(result.Problem);
        return result.Report!;
    }

    [Fact]
    public void A_valid_agent_has_no_diagnostics()
    {
        using var vault = new TempVault().Asset("agents/determinism-auditor", SampleManifests.Valid);

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
        using var vault = new TempVault().Asset(
            "agents/determinism-auditor",
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
        using var vault = new TempVault().Asset(
            "agents/determinism-auditor",
            SampleManifests.Valid.Replace("version: 0.1.0\n", ""));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Missing required field: version", diagnostic.Message);
        Assert.Equal(new SourceLocation(1, 1), diagnostic.Location);
    }

    [Fact]
    public void Missing_manifest_is_an_error()
    {
        using var vault = new TempVault().Write("agents/empty/agent.md", "# empty\n");

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
        using var vault = new TempVault()
            .Write("agents/determinism-auditor/asset.yml", SampleManifests.Valid)
            .Write("agents/determinism-auditor/agent.md", "# determinism-auditor\n");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Missing asset.yaml", diagnostic.Message);
        Assert.Equal(["Rename asset.yml to asset.yaml."], diagnostic.Detail);
    }

    [Fact]
    public void Manifest_name_must_match_exactly()
    {
        using var vault = new TempVault()
            .Write("agents/determinism-auditor/Asset.yaml", SampleManifests.Valid)
            .Write("agents/determinism-auditor/agent.md", "# determinism-auditor\n");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Missing asset.yaml", diagnostic.Message);
        Assert.Equal(["Rename Asset.yaml to asset.yaml."], diagnostic.Detail);
    }

    [Fact]
    public void Missing_content_file_is_an_error()
    {
        using var vault = new TempVault().Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid);

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal(Severity.Error, diagnostic.Severity);
        Assert.Equal("agents/determinism-auditor", diagnostic.File);
        Assert.Null(diagnostic.Location);
        Assert.Equal("Missing agent.md", diagnostic.Message);
        Assert.Equal(["An asset keeps its content in a Markdown file named after its kind."], diagnostic.Detail);
    }

    [Theory]
    [InlineData("agent", "agents", "agent.md")]
    [InlineData("skill", "skills", "skill.md")]
    [InlineData("hook", "hooks", "hook.md")]
    [InlineData("policy", "policies", "policy.md")]
    [InlineData("workflow", "workflows", "workflow.md")]
    [InlineData("experiment", "experiments", "experiment.md")]
    public void Each_kind_has_its_own_content_file(string kind, string folder, string contentFile)
    {
        using var vault = new TempVault().Write($"{folder}/x/asset.yaml", TempVault.Manifest(kind, "x"));

        Assert.Contains(Report(vault).Diagnostics, diagnostic => diagnostic.Message == $"Missing {contentFile}");
    }

    [Fact]
    public void Content_file_name_must_match_exactly()
    {
        using var vault = new TempVault()
            .Write("agents/determinism-auditor/asset.yaml", SampleManifests.Valid)
            .Write("agents/determinism-auditor/Agent.md", "# determinism-auditor\n");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Missing agent.md", diagnostic.Message);
        Assert.Equal(["Rename Agent.md to agent.md."], diagnostic.Detail);
    }

    [Fact]
    public void Blank_content_file_is_an_error()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Write("agents/determinism-auditor/agent.md", " \n\n");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("agents/determinism-auditor/agent.md", diagnostic.File);
        Assert.Null(diagnostic.Location);
        Assert.Equal("agent.md is empty", diagnostic.Message);
        Assert.Equal(["Write the asset's content in it."], diagnostic.Detail);
    }

    [Fact]
    public void Unreadable_content_file_is_an_error_and_the_rest_still_run()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Asset("agents/other-agent", TempVault.Manifest("agent", "other-agent"));
        using var locked = MakeUnreadable(Path.Combine(vault.Root, "agents", "determinism-auditor", "agent.md"));

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("agents/determinism-auditor/agent.md", diagnostic.File);
        Assert.StartsWith("Couldn't read agent.md: ", diagnostic.Message);
        Assert.Equal(["determinism-auditor", "other-agent"], report.Assets.Select(asset => asset.Name));
    }

    [Fact]
    public void Empty_manifest_is_an_error()
    {
        using var vault = new TempVault().Asset("agents/determinism-auditor", "");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("The file is empty.", diagnostic.Message);
    }

    [Fact]
    public void Name_must_match_the_folder()
    {
        using var vault = new TempVault().Asset("agents/other", SampleManifests.Valid);

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(new SourceLocation(1, 1), diagnostic.Location);
        Assert.Equal("name \"determinism-auditor\" doesn't match its folder \"other\"", diagnostic.Message);
    }

    [Fact]
    public void Kind_must_match_the_folder()
    {
        using var vault = new TempVault().Asset("skills/determinism-auditor", SampleManifests.Valid);

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal(new SourceLocation(2, 1), diagnostic.Location);
        Assert.Equal("kind \"agent\" doesn't match its folder \"skills/\"", diagnostic.Message);
        Assert.Equal(AssetKind.Skill, Assert.Single(report.Assets).Kind);
    }

    [Theory]
    [InlineData("skill", "skills")]
    [InlineData("hook", "hooks")]
    [InlineData("policy", "policies")]
    public void A_kind_with_its_block_is_healthy(string kind, string folder)
    {
        using var vault = new TempVault().Asset($"{folder}/x", TempVault.Manifest(kind, "x"));

        Assert.Empty(Report(vault).Diagnostics);
    }

    [Fact]
    public void A_skill_without_its_block_is_an_error()
    {
        using var vault = new TempVault().Asset("skills/x", TempVault.Manifest("skill", "x").Replace(SampleManifests.Blocks["skill"], ""));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("skills/x/asset.yaml", diagnostic.File);
        Assert.Equal(new SourceLocation(1, 1), diagnostic.Location);
        Assert.Equal("Missing required field: skill", diagnostic.Message);
        Assert.Equal(["The skill block in a skill's asset.yaml: when the skill should activate."], diagnostic.Detail);
    }

    [Fact]
    public void Block_errors_point_at_the_field_inside_the_block()
    {
        using var vault = new TempVault().Asset("hooks/x", TempVault.Manifest("hook", "x").Replace("response: warn", "response: shout"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(new SourceLocation(25, 3), diagnostic.Location);
        Assert.Equal("Unknown response: \"shout\"", diagnostic.Message);
        Assert.Equal(["Allowed: block, warn, evidence"], diagnostic.Detail);
    }

    [Fact]
    public void Another_kinds_block_is_an_error()
    {
        using var vault = new TempVault().Asset("agents/determinism-auditor", SampleManifests.Valid + SampleManifests.Blocks["skill"]);

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(new SourceLocation(22, 1), diagnostic.Location);
        Assert.Equal("Only skills have a skill block", diagnostic.Message);
        Assert.Equal(["This asset is in agents/. Remove the block, or move the asset to skills/."], diagnostic.Detail);
    }

    private static string PolicyEnforcedBy(string targets) =>
        TempVault.Manifest("policy", "p").Replace("enforced_by: []", $"enforced_by: [{targets}]");

    [Fact]
    public void Enforced_by_an_existing_asset_is_fine()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Asset("policies/p", PolicyEnforcedBy("agents/determinism-auditor"));

        Assert.Empty(Report(vault).Diagnostics);
    }

    [Fact]
    public void Enforced_by_must_name_an_existing_asset()
    {
        using var vault = new TempVault()
            .Asset("hooks/scope-sheriff", TempVault.Manifest("hook", "scope-sheriff"))
            .Asset("policies/p", PolicyEnforcedBy("hooks/scope-sherif"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("policies/p/asset.yaml", diagnostic.File);
        Assert.Equal(26, diagnostic.Location!.Value.Line);
        Assert.Equal("enforced_by names \"hooks/scope-sherif\", which doesn't exist", diagnostic.Message);
        Assert.Equal(["The vault's hooks: scope-sheriff"], diagnostic.Detail);
    }

    [Fact]
    public void Enforced_by_a_kind_with_no_assets_says_so()
    {
        using var vault = new TempVault().Asset("policies/p", PolicyEnforcedBy("workflows/review"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(["The vault has no workflows yet."], diagnostic.Detail);
    }

    [Fact]
    public void A_true_eval_flag_needs_eval_files()
    {
        using var vault = new TempVault().Asset("agents/determinism-auditor", SampleManifests.Valid.Replace("behavioral: false", "behavioral: true"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("agents/determinism-auditor/asset.yaml", diagnostic.File);
        Assert.Equal(new SourceLocation(20, 3), diagnostic.Location);
        Assert.Equal("evals.behavioral is true, but agents/determinism-auditor/evals/behavioral/ has no files", diagnostic.Message);
        Assert.Equal(["Add the behavioral evals there, or set evals.behavioral to false."], diagnostic.Detail);
    }

    [Fact]
    public void Eval_files_satisfy_a_true_eval_flag()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid.Replace("behavioral: false", "behavioral: true"))
            .Write("agents/determinism-auditor/evals/behavioral/billing/case-01.yaml", "prompt: x\n");

        Assert.Empty(Report(vault).Diagnostics);
    }

    [Fact]
    public void Hidden_files_are_not_evals()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid.Replace("regression: false", "regression: true"))
            .Write("agents/determinism-auditor/evals/regression/.gitkeep", "");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.StartsWith("evals.regression is true", diagnostic.Message);
    }

    [Fact]
    public void References_are_checked_once_the_manifest_is_valid()
    {
        using var vault = new TempVault().Asset(
            "agents/determinism-auditor",
            SampleManifests.Valid.Replace("behavioral: false", "behavioral: true").Replace("maturity: experimental", "maturity: production-ready"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Unknown maturity: \"production-ready\"", diagnostic.Message);
    }

    private static string Claiming(string maturity, string manifest, params string[] evals) =>
        evals.Aggregate(manifest.Replace("maturity: experimental", $"maturity: {maturity}"), (text, type) => text.Replace($"{type}: false", $"{type}: true"));

    private static TempVault WithEvalFiles(TempVault vault, string folder, params string[] types) =>
        types.Aggregate(vault, (v, type) => v.Write($"{folder}/evals/{type}/case-01.yaml", "prompt: x\n"));

    private static string UsageLog(string folder, params string[] repos) =>
        string.Concat(repos.Select((repo, i) => $"## 2026-10-{i + 1:00} · {repo}\n\nUsed [it](../{folder}/).\n\n"));

    [Fact]
    public void A_maturity_claim_without_its_evidence_is_an_error()
    {
        using var vault = new TempVault().Asset("agents/determinism-auditor", Claiming("tested", SampleManifests.Valid));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("agents/determinism-auditor/asset.yaml", diagnostic.File);
        Assert.Equal(new SourceLocation(5, 1), diagnostic.Location);
        Assert.Equal("maturity \"tested\" lacks its evidence", diagnostic.Message);
        Assert.Equal(
            [
                "Needs 1 usage entry in docs/dogfooding.md, found 0.",
                "Needs behavioral evals, and evals.behavioral isn't true.",
                "Needs regression evals, and evals.regression isn't true.",
                "The evidence supports experimental. Lower the maturity, or add the evidence.",
            ],
            diagnostic.Detail);
    }

    [Fact]
    public void Usage_and_evals_earn_tested()
    {
        using var vault = WithEvalFiles(
                new TempVault().Asset("agents/determinism-auditor", Claiming("tested", SampleManifests.Valid, "behavioral", "regression")),
                "agents/determinism-auditor",
                "behavioral",
                "regression")
            .Write("docs/dogfooding.md", UsageLog("agents/determinism-auditor", "carmine-workbench"));

        Assert.Empty(Report(vault).Diagnostics);
    }

    [Fact]
    public void A_skill_also_needs_trigger_evals()
    {
        using var vault = WithEvalFiles(
                new TempVault().Asset("skills/x", Claiming("tested", TempVault.Manifest("skill", "x"), "behavioral", "regression")),
                "skills/x",
                "behavioral",
                "regression")
            .Write("docs/dogfooding.md", UsageLog("skills/x", "carmine-workbench"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(
            ["Needs trigger evals, and evals.trigger isn't true.", "The evidence supports incubating. Lower the maturity, or add the evidence."],
            diagnostic.Detail);
    }

    [Fact]
    public void Battle_tested_counts_different_repos()
    {
        using var vault = WithEvalFiles(
                new TempVault().Asset("agents/determinism-auditor", Claiming("battle-tested", SampleManifests.Valid, "behavioral", "regression")),
                "agents/determinism-auditor",
                "behavioral",
                "regression")
            .Write("docs/dogfooding.md", UsageLog("agents/determinism-auditor", "a", "a", "b", "b", "b"));

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal(
            ["Needs 3 repos in its usage entries, found 2.", "The evidence supports stable. Lower the maturity, or add the evidence."],
            diagnostic.Detail);
    }

    [Fact]
    public void Unreadable_usage_log_is_an_error_and_the_assets_are_still_checked()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Write("docs/dogfooding.md", "## 2026-10-01 · x\n");
        using var locked = MakeUnreadable(Path.Combine(vault.Root, "docs", "dogfooding.md"));

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("docs/dogfooding.md", diagnostic.File);
        Assert.StartsWith("Couldn't read docs/dogfooding.md: ", diagnostic.Message);
        Assert.Equal("determinism-auditor", Assert.Single(report.Assets).Name);
    }

    [Fact]
    public void Duplicate_key_is_reported_with_its_line()
    {
        using var vault = new TempVault().Asset("agents/determinism-auditor", "name: a\nname: b\n");

        var diagnostic = Assert.Single(Report(vault).Diagnostics);

        Assert.Equal("Duplicate key \"name\".", diagnostic.Message);
        Assert.Equal(2, diagnostic.Location!.Value.Line);
    }

    [Fact]
    public void Unreadable_manifest_is_an_error_and_the_rest_still_run()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
            .Asset("agents/other-agent", TempVault.Manifest("agent", "other-agent"));
        using var locked = MakeUnreadable(Path.Combine(vault.Root, "agents", "determinism-auditor", "asset.yaml"));

        var report = Report(vault);

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("agents/determinism-auditor/asset.yaml", diagnostic.File);
        Assert.StartsWith("Couldn't read asset.yaml: ", diagnostic.Message);
        Assert.Equal(["determinism-auditor", "other-agent"], report.Assets.Select(asset => asset.Name));
    }

    // Windows enforces an exclusive lock; elsewhere a file with no permissions can't be read.
    private static IDisposable? MakeUnreadable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        File.SetUnixFileMode(path, UnixFileMode.None);
        return null;
    }

    [Fact]
    public void Folder_without_a_vault_could_not_run()
    {
        using var vault = new TempVault().Folder("docs");

        var result = Doctor.Run(vault.Root);

        Assert.Null(result.Report);
        Assert.Equal(VaultProblemKind.NotAVault, result.Problem!.Kind);
        Assert.StartsWith($"No vault found in {vault.Root}.", result.Problem.Message);
    }

    [Fact]
    public void Missing_root_could_not_run()
    {
        var missing = Path.Combine(Path.GetTempPath(), "axm-does-not-exist-" + Guid.NewGuid().ToString("N"));

        var result = Doctor.Run(missing);

        Assert.Equal(VaultProblemKind.FolderMissing, result.Problem!.Kind);
        Assert.Equal($"The folder {missing} does not exist.", result.Problem.Message);
    }

    [Fact]
    public void Root_that_is_a_file_could_not_run()
    {
        using var vault = new TempVault().Write("notes.md", "# notes\n");
        var file = Path.Combine(vault.Root, "notes.md");

        var result = Doctor.Run(file);

        Assert.Equal(VaultProblemKind.NotAFolder, result.Problem!.Kind);
        Assert.Equal($"{file} is a file, not a folder.", result.Problem.Message);
    }

    [Fact]
    public void Hidden_folders_and_loose_files_are_ignored()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid)
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
            .Asset("skills/b", TempVault.Manifest("skill", "b"))
            .Asset("agents/z", TempVault.Manifest("agent", "z"))
            .Asset("agents/a", TempVault.Manifest("agent", "a"));

        var report = Report(vault);

        Assert.Empty(report.Diagnostics);
        Assert.Equal(["agents/a", "agents/z", "skills/b"], report.Assets.Select(asset => asset.Folder));
    }

    [Fact]
    public void Paths_use_forward_slashes()
    {
        using var vault = new TempVault()
            .Asset("agents/determinism-auditor", SampleManifests.Valid.Replace("kind: agent", "kind: nope"))
            .Write("hooks/empty/hook.md", "# empty\n");

        var report = Report(vault);

        Assert.All(report.Diagnostics, diagnostic => Assert.DoesNotContain('\\', diagnostic.File));
        Assert.All(report.Assets, asset => Assert.DoesNotContain('\\', asset.Folder));
        Assert.Equal(2, report.ErrorCount);
    }
}
