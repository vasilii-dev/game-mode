namespace GameMode.Core;

public static class AppVersion
{
    public const string Display = "Beta 0.1";
    public const string Numeric = "0.1.0";
    public static Version Current { get; } = new(0, 1, 0);
}
