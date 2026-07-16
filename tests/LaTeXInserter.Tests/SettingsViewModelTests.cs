using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.ViewModels;
using NSubstitute;
using SharpHook.Data;
using Xunit;

namespace LaTeXInserter.Tests;

public class SettingsViewModelTests
{
    private static AppSettings TestSettings() => new()
    {
        Hotkey = new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, KeyCode.VcM),
        StartOnStartup = false,
        InputFontSize = 16,
        PreviewFontSize = 20,
        AccentColor = "#404040",
        AutocompleteEnabled = true
    };

    private static ISettingsService CreateSettings(AppSettings settings)
    {
        var mock = Substitute.For<ISettingsService>();
        // Service is the source of truth — Load returns the latest saved value.
        var current = settings;
        mock.Load().Returns(_ => current);
        mock.When(s => s.Save(Arg.Any<AppSettings>()))
            .Do(c => current = c.Arg<AppSettings>());
        return mock;
    }

    private static IHotkeyService CreateHotkey() => Substitute.For<IHotkeyService>();

    private static IStartupRegistrar CreateStartup() => Substitute.For<IStartupRegistrar>();

    private static IAccentColorModule CreateAccent()
    {
        var mock = Substitute.For<IAccentColorModule>();
        // Default no-op Apply; tests inspect received calls.
        return mock;
    }

    [Fact]
    public void Open_LoadsValuesFromSettings()
    {
        var settings = TestSettings() with { InputFontSize = 18, AccentColor = "#3B82F6", AutocompleteEnabled = false };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent());

        Assert.Equal(18, vm.InputFontSize);
        Assert.Equal("#3B82F6", vm.AccentColor);
        Assert.False(vm.AutocompleteEnabled);
    }

    [Fact]
    public async Task Save_PersistsAndAppliesAccentAndRaisesSettingsSaved()
    {
        var svc = CreateSettings(TestSettings());
        var startup = CreateStartup();
        var accent = CreateAccent();
        var vm = new SettingsViewModel(svc, CreateHotkey(), startup, accent);

        vm.AccentColor = "#EF4444";
        vm.AutocompleteEnabled = false;
        AppSettings? captured = null;
        vm.SettingsSaved += (_, s) => captured = s;

        await vm.SaveCommand.ExecuteAsync(null);

        // Persisted via service Save with new values.
        svc.Received(1).Save(Arg.Is<AppSettings>(s =>
            s.AccentColor == "#EF4444" && s.AutocompleteEnabled == false));
        Assert.NotNull(captured);
        Assert.Equal("#EF4444", captured!.AccentColor);
        // Accent module apply (live effect) called with the new value on save:
        accent.Received().Apply("#EF4444");
        await startup.Received().SyncRegistrationAsync(false);
    }

    [Fact]
    public void Cancel_DoesNotPersistAndRevertsLiveAccentPreview()
    {
        var svc = CreateSettings(TestSettings());
        var accent = CreateAccent();
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), accent);

        vm.AccentColor = "#EF4444";
        // Simulate close via Cancel: CloseRequested fires, AppManager closes window, OnClosed reverts.
        var closeFired = false;
        vm.CloseRequested += (_, _) => closeFired = true;
        vm.CancelCommand.Execute(null);
        vm.OnClosed();

        Assert.True(closeFired);
        svc.DidNotReceive().Save(Arg.Any<AppSettings>());
        // Live accent preview reverted to the persisted value.
        accent.Received().Apply("#404040");
    }

    [Fact]
    public void OnClosed_RevertsUnsavedFieldChanges()
    {
        var svc = CreateSettings(TestSettings());
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent());

        var originalFont = vm.InputFontSize;
        vm.InputFontSize = 24;
        vm.AutocompleteEnabled = false;
        vm.StartOnStartup = true;

        vm.OnClosed();

        Assert.Equal(originalFont, vm.InputFontSize);
        Assert.True(vm.AutocompleteEnabled);
        Assert.False(vm.StartOnStartup);
        svc.DidNotReceive().Save(Arg.Any<AppSettings>());
    }

    [Fact]
    public void OnClosed_AfterSave_DoesNotRevert()
    {
        var svc = CreateSettings(TestSettings());
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent());

        vm.InputFontSize = 22;
        vm.AccentColor = "#10B981";
        vm.SaveCommand.Execute(null);
        vm.OnClosed();

        // Values persist after save.
        Assert.Equal(22, vm.InputFontSize);
        Assert.Equal("#10B981", vm.AccentColor);
        svc.Received(1).Save(Arg.Any<AppSettings>());
    }

    [Fact]
    public void SelectSwatch_SetsAccentColor_WithoutLiveApplyOrPersist()
    {
        var svc = CreateSettings(TestSettings());
        var accent = CreateAccent();
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), accent);

        // Clear ctor-time apply received from Open().
        accent.ClearReceivedCalls();

        var swatch = SettingsViewModel.AccentPalette.First(s => s.Hex == "#EF4444");
        vm.SelectSwatch(swatch);

        Assert.Equal("#EF4444", vm.AccentColor);
        Assert.True(swatch.IsSelected);
        // Swatch click must NOT immediately apply (no live recolor) or persist.
        accent.DidNotReceive().Apply(Arg.Any<string>());
        svc.DidNotReceive().Save(Arg.Any<AppSettings>());
    }

    [Fact]
    public void Open_AfterUnsavedCancel_ReflectsDisk()
    {
        // Singleton VM: after Cancel reverts, reopening refreshes from disk.
        var svc = CreateSettings(TestSettings());
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent());

        vm.InputFontSize = 24;
        vm.OnClosed(); // revert
        vm.Open();     // reopen

        Assert.Equal(16, vm.InputFontSize);
    }
}
