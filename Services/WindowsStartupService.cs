#if WINDOWS || NET10_0_WINDOWS10_0_26100_0_OR_GREATER
using Microsoft.Win32;
#endif

namespace OpenQCY_Desktop.Services;

public sealed class WindowsStartupService
{
    internal const string StartupArgument = "--startup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "OpenQCY Desktop";

    public bool IsEnabled
    {
        get
        {
#if WINDOWS || NET10_0_WINDOWS10_0_26100_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var registeredCommand = runKey?.GetValue(RunValueName) as string;
            return string.Equals(
                registeredCommand,
                BuildStartupCommand(GetExecutablePath()),
                StringComparison.OrdinalIgnoreCase);
#else
            return false;
#endif
        }
    }

    public void SetEnabled(bool enabled)
    {
#if WINDOWS || NET10_0_WINDOWS10_0_26100_0_OR_GREATER
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not access Windows startup settings.");

        if (enabled)
        {
            runKey.SetValue(
                RunValueName,
                BuildStartupCommand(GetExecutablePath()),
                RegistryValueKind.String);
        }
        else
        {
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
#endif
    }

    internal static bool IsStartupLaunch(IReadOnlyList<string> commandLineArguments) =>
        commandLineArguments
            .Skip(1)
            .Any(argument => string.Equals(argument, StartupArgument, StringComparison.OrdinalIgnoreCase));

    internal static string BuildStartupCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"'))
        {
            throw new ArgumentException("The application path contains quotation marks.", nameof(executablePath));
        }

        return $"\"{executablePath}\" {StartupArgument}";
    }

    private static string GetExecutablePath() =>
        Environment.ProcessPath
        ?? throw new InvalidOperationException("The OpenQCY Desktop path is unavailable.");
}
