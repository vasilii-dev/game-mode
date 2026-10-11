using GameMode.Core;
using Microsoft.Win32;

namespace GameMode.Windows.Processes;

internal sealed class SteamGameDiscovery
{
    private readonly object _gate = new();
    private SteamManifestIndex _index = SteamManifestIndex.Read([]);
    private long _nextRefresh;

    public SteamManifestIndex Snapshot()
    {
        lock (_gate)
        {
            if (Environment.TickCount64 < _nextRefresh) return _index;
            _nextRefresh = Environment.TickCount64 + 60000;
            try
            {
                using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var roots = new[] { steam?.GetValue("SteamPath") as string, steam?.GetValue("InstallPath") as string }
                    .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Replace('/', '\\')).ToList();
                var defaultPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
                if (System.IO.Directory.Exists(defaultPath)) roots.Add(defaultPath);
                _index = SteamManifestIndex.Read(roots);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { _index = SteamManifestIndex.Read([]); }
            return _index;
        }
    }
}
