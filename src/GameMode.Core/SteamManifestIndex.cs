using System.Text.RegularExpressions;

namespace GameMode.Core;

public sealed class SteamManifestIndex
{
    private readonly List<(string Root, string Label)> _games = [];
    public IReadOnlyList<string> Roots => _games.Select(g => g.Root).ToArray();

    public static SteamManifestIndex Read(IEnumerable<string> steamDirectories)
    {
        var result = new SteamManifestIndex();
        foreach (var steam in steamDirectories.Select(Normalize).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(16))
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(steam) };
            var libraryFile = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            var libraryText = ReadSmall(libraryFile);
            foreach (Match match in Regex.Matches(libraryText, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (Normalize(path) is { } absolute) libraries.Add(absolute);
            }
            foreach (var library in libraries.Take(32))
            {
                var apps = Path.Combine(library, "steamapps");
                try
                {
                    if (!Directory.Exists(apps)) continue;
                    foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf").Take(1000))
                    {
                        var text = ReadSmall(file);
                        var install = Field(text, "installdir");
                        if (string.IsNullOrWhiteSpace(install) || Path.IsPathFullyQualified(install)) continue;
                        var common = Path.GetFullPath(Path.Combine(apps, "common")) + Path.DirectorySeparatorChar;
                        var root = Path.GetFullPath(Path.Combine(common, install));
                        if (!root.StartsWith(common, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(root)) continue;
                        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
                        var name = Field(text, "name");
                        if (name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) || name.Contains("Steamworks", StringComparison.OrdinalIgnoreCase)) continue;
                        result._games.Add((root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, "Steam · " + (name.Length > 0 ? name : install)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        return result;
    }

    public string? Evidence(string? executable)
    {
        if (executable == null || !Path.IsPathFullyQualified(executable)) return null;
        var name = Path.GetFileNameWithoutExtension(executable);
        if (Regex.IsMatch(name, "^(?:unins|crash|unitycrash|setup|install|steamwebhelper|easyanticheat|beservice|bootstrapper)", RegexOptions.IgnoreCase)) return null;
        if (executable.Contains("_CommonRedist", StringComparison.OrdinalIgnoreCase) || executable.Contains("\\redist\\", StringComparison.OrdinalIgnoreCase)) return null;
        return _games.FirstOrDefault(g => executable.StartsWith(g.Root, StringComparison.OrdinalIgnoreCase)).Label;
    }

    private static string Field(string text, string field) => Regex.Match(text, "\"" + Regex.Escape(field) + "\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase).Groups[1].Value;
    private static string? Normalize(string path)
    {
        try { return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null; }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
    }
    private static string ReadSmall(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length <= 131072 ? File.ReadAllText(path) : ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }
}
