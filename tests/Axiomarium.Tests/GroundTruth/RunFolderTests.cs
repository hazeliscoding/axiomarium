using Axiomarium.GroundTruth;

namespace Axiomarium.Tests.GroundTruth;

public class RunFolderTests
{
    // Codex matches trust keys and project paths only as it writes them, with backslashes on Windows, so a scenario's
    // {run} paths become paths in the platform's form.
    [Fact]
    public void A_run_path_in_a_config_file_takes_the_platforms_form()
    {
        var run = OperatingSystem.IsWindows() ? @"C:\axm-ground-truth\run" : "/tmp/axm-ground-truth/run";
        const string Config = "[projects.'{run}/repo']\n\n[hooks.state.'{run}/home/.codex/hooks.json:session_start:1:0']\n";

        Assert.Equal(
            $"[projects.'{Path.Combine(run, "repo")}']\n\n[hooks.state.'{Path.Combine(run, "home", ".codex", "hooks.json")}:session_start:1:0']\n",
            RunFolder.FillIn(Config, run));
    }
}
