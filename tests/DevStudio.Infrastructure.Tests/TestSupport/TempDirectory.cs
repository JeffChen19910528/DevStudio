namespace DevStudio.Infrastructure.Tests.TestSupport;

/// <summary>Creates a throwaway directory under the OS temp folder and deletes it on dispose —
/// tests must never write into the real DevStudio repository (SKILL.md §29).</summary>
public sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DevStudioTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void CreateDirectory(string relativePath) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath));

    public void Dispose()
    {
        // A just-killed child process (e.g. a real debugger/debuggee in the Phase 6 debug
        // tests) can hold a file handle open for a few milliseconds after Process.Kill()
        // returns, even though the process itself is gone — Windows releases the handle
        // asynchronously. Retry briefly instead of failing the whole test on that race.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                ClearReadOnlyAttributes(Path);
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < 20)
            {
                Thread.Sleep(250);
            }
            catch (Exception) when (attempt >= 20)
            {
                // A real debugger (Phase 6) can leave a build output's file lock in a state
                // that clears only after this whole test process has exited (observed with
                // netcoredbg-debugged binaries specifically) — likely Windows' own delayed
                // release of an executable image section, or real-time AV scanning the freshly
                // produced binary, neither of which DevStudio's own code controls. Failing to
                // delete an OS-temp-folder scratch directory is not a test failure in itself
                // (nothing here asserts on cleanup), so this is a deliberate best-effort give-up
                // rather than letting an unrelated OS/AV timing quirk fail an otherwise-passing
                // real-debugger test. The OS temp folder is still cleaned up eventually by the
                // OS itself.
                return;
            }
        }
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (IOException)
            {
                // Still locked — the delete retry loop will catch this on its next attempt.
            }
        }
    }
}
