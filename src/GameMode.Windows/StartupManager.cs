using System.IO;
using Microsoft.Win32;

namespace GameMode.Windows;

public static class StartupManager
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "GameMode";
    public static string Status { get; private set; } = "Для текущего пользователя, без прав администратора.";
    private static string? _lastCommand;

    public static string? Apply(bool enabled)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (enabled && (executable == null || !string.Equals(Path.GetFileName(executable), "GameMode.exe", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Автозагрузка доступна при запуске GameMode.exe; запустите опубликованное приложение.");
            var command = enabled ? $"\"{executable}\" --startup" : "";
            if (command.Length > 260) throw new InvalidOperationException("Для автозагрузки переместите приложение в папку с более коротким путём.");
            if (_lastCommand == command) return null;
            using var key = enabled ? Registry.CurrentUser.CreateSubKey(RunKey, writable: true) : Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (enabled) key!.SetValue(ValueName, command, RegistryValueKind.String);
            else key?.DeleteValue(ValueName, throwOnMissingValue: false);
            _lastCommand = command;
            Status = enabled ? "Game Mode запускается в трее при входе в Windows. Диспетчер задач может отдельно отключить автозагрузку." : "Запуск при входе в Windows отключён.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or ArgumentException)
        { Status = "Автозагрузка недоступна: " + ex.Message; return Status; }
    }
}
