using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Services;
using LaTeXInserter.Platform.Windows;
using LaTeXInserter.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SharpHook;
using Velopack;

namespace LaTeXInserter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var services = new ServiceCollection();
        ConfigureServices(services);
        var serviceProvider = services.BuildServiceProvider();

        App.SetServiceProvider(serviceProvider);

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILatexConverterService, LatexConverterService>();
        services.AddSingleton<IAccentColorModule, AccentColorModule>();

        // SharpHook
        services.AddSingleton<SimpleGlobalHook>();
        services.AddSingleton<IEventSimulator, EventSimulator>();

        // Phase 2 services
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IClipboardProvider, AvaloniaClipboardProvider>();
        services.AddSingleton<IInputSimulatorService, InputSimulatorService>();
        services.AddSingleton<IWindowActivator, WindowsWindowActivator>();
        services.AddSingleton<IOverlayPositioner, WindowsOverlayPositioner>();
        services.AddSingleton<ISubmitPasteService, SubmitPasteService>();
        services.AddSingleton<IStartupRegistrar, WindowsStartupRegistrar>();

        // Phase 3 — tray & lifecycle
        services.AddSingleton<TrayIconViewModel>();

        // Phase 4 — overlay & dialogs
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<UpToDateViewModel>();
        services.AddSingleton<UpdateViewModel>();

        // Phase 5 — hotkey dialog
        services.AddSingleton<HotkeyDialogViewModel>();

        // Settings
        services.AddSingleton<SettingsViewModel>();

        // Custom Mappings
        services.AddSingleton<CustomMappingsViewModel>();

        // Phase 6 — updates
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IUpdateCoordinator, UpdateCoordinator>();

        services.AddSingleton<AppManager>();
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
