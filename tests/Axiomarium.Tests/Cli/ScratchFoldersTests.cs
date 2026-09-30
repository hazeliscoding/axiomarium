using Axiomarium.Cli;

namespace Axiomarium.Tests.Cli;

public class ScratchFoldersTests
{
    // Git makes its object files read-only, which stops a plain recursive delete on Windows.
    [Fact]
    public void A_folder_with_read_only_files_is_deleted()
    {
        using var root = new TempVault().Write("copy-1/.git/objects/ab/cdef", "blob").Write("copy-1/app.ts", "a");
        var folder = Path.Combine(root.Root, "copy-1");
        var locked = Path.Combine(folder, ".git", "objects", "ab", "cdef");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        var deleted = ScratchFolders.Delete(folder);

        Assert.True(deleted);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void A_leftover_with_read_only_files_is_removed()
    {
        using var scratch = new TempVault().Write("axm-evals/20260929-dead/copy-1/.git/objects/ab/cdef", "blob");
        File.SetAttributes(Path.Combine(scratch.Root, "axm-evals", "20260929-dead", "copy-1", ".git", "objects", "ab", "cdef"), FileAttributes.ReadOnly);

        var removed = new ProcessHarnessRunner(scratch.Root).RemoveLeftovers("evals");

        Assert.Equal(["20260929-dead"], removed);
    }
}
