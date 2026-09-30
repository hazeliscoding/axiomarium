using Axiomarium.Core.Evals;

namespace Axiomarium.Tests.Core;

// Codex's sandbox reads the whole disk on Windows, so a run can read the answers from a real repo. The report says so.
public class OutsidePathsTests
{
    private const string Copy = @"C:\axm-evals\run\copy-1";

    [Fact]
    public void Absolute_paths_outside_the_copy_are_found_in_any_command()
    {
        string[] commands =
        [
            @"rg --files C:\\ai\\axiomarium -g asset.yaml",
            @"Get-Content C:\ai\axiomarium\README.md; Get-Content 'C:\Users\dev\.codex\auth.json'",
            "cat /home/dev/.ssh/id_rsa",
        ];

        Assert.Equal(
            [@"C:\ai\axiomarium", @"C:\ai\axiomarium\README.md", @"C:\Users\dev\.codex\auth.json", "/home/dev/.ssh/id_rsa"],
            OutsidePaths.Of(commands, Copy, []));
    }

    // Claude Code's Bash on Windows is Git Bash, which writes C:\axm-evals as /c/axm-evals.
    [Fact]
    public void A_git_bash_path_is_read_as_its_windows_path()
    {
        string[] commands = ["cat /c/axm-evals/run/copy-1/src/llm.ts; cat /c/ai/axiomarium/README.md"];

        Assert.Equal([@"C:\ai\axiomarium\README.md"], OutsidePaths.Of(commands, Copy, []));
    }

    // The copy, the axm the session runs, the system's own folders and the null device are where work happens anyway.
    [Fact]
    public void The_copy_the_tools_and_the_system_s_folders_are_not_outside()
    {
        string[] commands =
        [
            @"Get-Content C:\axm-evals\run\copy-1\app.ts; Get-Content c:/AXM-EVALS/run/copy-1/b.md",
            @"& 'C:\Tools\axm\axm.exe' validate",
            @"C:\Windows\System32\where.exe git; & 'C:\Program Files\Git\bin\git.exe' status",
            @"curl.exe -s -o NUL https://example.com; echo hi > /dev/null",
            "/usr/bin/env bash -c 'ls'",
            "rg --files -g '!/.git/**' -g '!/.agents' .",
        ];

        Assert.Empty(OutsidePaths.Of(commands, Copy, [@"C:\Tools\axm"]));
    }
}
