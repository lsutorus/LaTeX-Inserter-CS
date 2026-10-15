using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LaTeXInserter;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    internal static void SetServiceProvider(IServiceProvider sp) => Services = sp;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var trayIconViewModel = Services.GetRequiredService<TrayIconViewModel>();
            var appManager = Services.GetRequiredService<AppManager>();

            // Wire tray menu from ViewModel
            var trayIcon = TrayIcon.GetIcons(this)?[0];
            if (trayIcon is not null)
            {
                trayIcon.Menu = trayIconViewModel.TrayMenu;
                trayIcon.Icon = LoadTrayIcon();
            }

            // Apply saved accent color so Fluent theme cascades it
            var settingsService = Services.GetRequiredService<ISettingsService>();
            var accentModule = Services.GetRequiredService<IAccentColorModule>();
            accentModule.Apply(settingsService.Load().AccentColor);

            // Safety net — dispose AppManager on app exit
            desktop.Exit += (_, _) => appManager.Dispose();

            // Fire-and-forget startup
            _ = InitializeAsync(appManager);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Windows keeps the multi-resolution .ico. macOS needs a small PNG — Avalonia
    /// cannot decode .ico into an NSStatusItem image reliably, and the menu bar
    /// wants a ~18pt glyph rather than a full-colour app icon.
    /// </summary>
    private static WindowIcon LoadTrayIcon()
    {
        var uri = PlatformKinds.Current == PlatformKind.MacOS
            ? new Uri("avares://LaTeXInserter/Assets/tray-macos.png")
            : new Uri("avares://LaTeXInserter/Assets/LaTeX-Inserter-icon-final.ico");

        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }

    private static async Task InitializeAsync(AppManager appManager)
    {
        try
        {
            await appManager.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"App initialization failed: {ex}");
        }
    }
}
