using System;
using MicroSnip.Infrastructure;
using Microsoft.Win32;

namespace MicroSnip.Settings;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = AppDataPaths.ProductName;
    private const string LegacyValueName = AppDataPaths.LegacyProductName;

    internal static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            return;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("MicroSnip could not determine its executable path.");
        }

        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
