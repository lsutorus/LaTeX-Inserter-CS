using System.Diagnostics;
using System.Runtime.InteropServices;
using LaTeXInserter.Abstractions;
using Microsoft.Win32;

namespace LaTeXInserter.Platform.Windows;

internal sealed class WindowsStartupRegistrar : IStartupRegistrar
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LaTeX Inserter";

    public Task<bool> GetIsRegisteredAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return Task.FromResult(value is not null);
    }

    public Task RegisterAsync()
    {
        var exePath = GetExePath();
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
        key.SetValue(ValueName, $"\"{exePath}\"");
        return Task.CompletedTask;
    }

    public Task UnregisterAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        return Task.CompletedTask;
    }

    public async Task SyncRegistrationAsync(bool desired)
    {
        var isRegistered = await GetIsRegisteredAsync();
        if (desired && !isRegistered)
            await RegisterAsync();
        else if (!desired && isRegistered)
            await UnregisterAsync();
    }

    private static string GetExePath()
    {
        // Velopack installed context: use root stub executable
        // Fallback: current process main module
        return Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot determine executable path.");
    }
}
