namespace ScreenTrail.Core.Store;

/// <summary>
/// The rules for the file the store key is kept in, apart from how the key is protected (DPAPI on Windows),
/// so the Mac suite can hold them (F-10).
///
/// A key file is written once and never replaced while there is anything it could open. The one exception
/// is a file that holds no key at all (empty, or zeros, as a power cut during the first write leaves it)
/// beside a store that holds nothing either: there is nothing a key could open, so it is "no key yet".
/// </summary>
public static class StoreKeyFile
{
    /// <summary>Larger than any protected key; a file this size or more is never read as "no key yet".</summary>
    private const long LargestUnwritten = 4_096;

    /// <summary>
    /// Whether <paramref name="keyFile"/> is there and holds no key (empty or all zeros), while the store
    /// beside it, and its log, are absent or empty. Only then may a new key take its place.
    /// </summary>
    public static bool NoKeyYet(string keyFile, string storeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeFile);
        var key = new FileInfo(keyFile);
        if (!key.Exists || key.Length >= LargestUnwritten || HoldsAnything(storeFile) || HoldsAnything(storeFile + "-wal"))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(keyFile);
        return Array.TrueForAll(bytes, b => b == 0);
    }

    /// <summary>
    /// Writes <paramref name="contents"/> to a file of its own beside <paramref name="path"/>, flushed to the
    /// disk, then moves it into place without overwriting. False when a file is already there (another
    /// process won the race): its key is the one to use, and nothing of this one is left behind.
    /// </summary>
    public static bool TryPlace(string path, ReadOnlySpan<byte> contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporary, path, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// Takes away a key file that <see cref="NoKeyYet"/> found holds no key, so a new one can be placed.
    /// Checked again just before, so a key another process placed meanwhile is never removed.
    /// </summary>
    public static void RemoveUnwritten(string keyFile, string storeFile)
    {
        if (NoKeyYet(keyFile, storeFile))
        {
            File.Delete(keyFile);
        }
    }

    private static bool HoldsAnything(string path)
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length > 0;
    }
}
