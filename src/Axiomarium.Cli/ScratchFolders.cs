namespace Axiomarium.Cli;

/// <summary>Deletes the throwaway folders harness runs work in.</summary>
internal static class ScratchFolders
{
    /// <summary>Deletes <paramref name="folder"/> and everything in it, read-only files included.</summary>
    /// <param name="folder">The folder. One that doesn't exist counts as deleted.</param>
    /// <returns>Whether it's gone. A file another process holds open can keep it, and then it returns <see langword="false"/>.</returns>
    public static bool Delete(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return true;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Git makes its object files read-only, which a recursive delete can't remove on Windows.
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
    }
}
