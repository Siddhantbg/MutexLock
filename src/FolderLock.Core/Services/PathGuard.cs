namespace FolderLock.Core.Services;

public static class PathGuard
{
    public static bool TryGetBlockReason(string path, out string reason)
    {
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Path is empty.";
            return true;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var separator = Path.DirectorySeparatorChar;

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(Path.TrimEndingDirectorySeparator(root), full, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Cannot lock a drive root.";
            return true;
        }

        foreach (var item in GetProtectedFolders())
        {
            if (string.Equals(item, full, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Cannot lock an important system or user folder: {item}";
                return true;
            }

            if (item.StartsWith(full + separator, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"This folder contains a protected folder ({item}) and cannot be locked as a whole.";
                return true;
            }

            if (IsSystemArea(item) && full.StartsWith(item + separator, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"Cannot lock folders inside a system folder: {item}";
                return true;
            }
        }

        return false;
    }

    public static void EnsureLockable(string path)
    {
        if (TryGetBlockReason(path, out var reason))
        {
            throw new FolderLockException(reason);
        }
    }

    private static bool IsSystemArea(string folder)
    {
        return string.Equals(folder, GetFolder(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)
               || string.Equals(folder, GetFolder(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
               || string.Equals(folder, GetFolder(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)
               || string.Equals(folder, GetFolder(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetProtectedFolders()
    {
        var folders = new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.Favorites,
        };

        foreach (var folder in folders)
        {
            var value = GetFolder(folder);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return Path.TrimEndingDirectorySeparator(value);
            }
        }
    }

    private static string GetFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch
        {
            return string.Empty;
        }
    }
}
