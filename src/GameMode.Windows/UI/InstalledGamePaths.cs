using System.Collections.Concurrent;
using System.IO;
using GameMode.Windows.Processes;

namespace GameMode.Windows.UI;

// Icon lookup only: installed files never become detection rules or cleanup targets.
internal static class InstalledGamePaths
{
    private static readonly ConcurrentDictionary<string, Task<string?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SteamGameDiscovery Steam = new();

    public static Task<string?> FindAsync(string executableName)
    {
        if (executableName is not ("dota2.exe" or "cs2.exe" or "RobloxPlayerBeta.exe")) return Task.FromResult<string?>(null);
        return Cache.GetOrAdd(executableName, static name => Task.Run(() =>
        {
            try
            {
                if (name != "RobloxPlayerBeta.exe") return FindSteamExecutable(Steam.Snapshot().Roots, name);
                var versions = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                if (!Directory.Exists(versions)) return null;
                return Directory.EnumerateDirectories(versions, "version-*").Take(64)
                    .Where(d => (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { return null; }
        }));
    }

    internal static string? FindSteamExecutable(IEnumerable<string> installedRoots, string executableName)
    {
        if (executableName is not ("dota2.exe" or "cs2.exe")) return null;
        foreach (var root in installedRoots.Take(256))
        {
            var path = Path.Combine(root, "game", "bin", "win64", executableName);
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
