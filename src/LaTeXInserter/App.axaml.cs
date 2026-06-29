using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using LaTeXInserter.Abstractions;
using LaTeXInserter.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LaTeXInserter;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    internal static void SetServiceProvider(IServiceProvider sp) => Services = sp;

    /// <summary>
    /// Sets SystemAccentColor + AccentBgBrush in Application.Resources.
    /// The Fluent theme auto-cascades SystemAccentColor to all accent-derived brushes
    /// (TextControlBorderBrushFocused, SystemControlHighlightAccentBrush, etc.).
    /// Call on startup and whenever settings change.
    /// </summary>
    public static void ApplyAccentColor(string hex)
    {
        var color = Color.Parse(hex);
        Current.Resources["SystemAccentColor"] = color;
        Current.Resources["AccentBgBrush"] = new SolidColorBrush(color, 0.25);
    }

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
                trayIcon.Menu = trayIconViewModel.TrayMenu;

            // Apply saved accent color so Fluent theme cascades it
            var settingsService = Services.GetRequiredService<ISettingsService>();
            ApplyAccentColor(settingsService.Load().AccentColor);

            // Safety net — dispose AppManager on app exit
            desktop.Exit += (_, _) => appManager.Dispose();

            // Fire-and-forget startup
            _ = InitializeAsync(appManager);
        }

        base.OnFrameworkInitializationCompleted();
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
