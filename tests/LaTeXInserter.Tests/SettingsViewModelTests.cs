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

    // Default: a platform that never needs permissions (Windows), so the panel stays hidden.
    private static IPermissionService CreatePermissions()
    {
        var mock = Substitute.For<IPermissionService>();
        mock.RequiresUserAction.Returns(false);
        mock.Query().Returns(PermissionStatus.AllGranted);
        return mock;
    }

    private static SettingsViewModel CreateSut(IPermissionService? permissions = null) =>
        new(CreateSettings(TestSettings()), CreateHotkey(), CreateStartup(), CreateAccent(),
            permissions ?? CreatePermissions());

    [Fact]
    public void Open_LoadsValuesFromSettings()
    {
        var settings = TestSettings() with { InputFontSize = 18, AccentColor = "#3B82F6", AutocompleteEnabled = false };
        var svc = CreateSettings(settings);
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent(), CreatePermissions());

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
        var vm = new SettingsViewModel(svc, CreateHotkey(), startup, accent, CreatePermissions());

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
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), accent, CreatePermissions());

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
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent(), CreatePermissions());

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
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent(), CreatePermissions());

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
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), accent, CreatePermissions());

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
        var vm = new SettingsViewModel(svc, CreateHotkey(), CreateStartup(), CreateAccent(), CreatePermissions());

        vm.InputFontSize = 24;
        vm.OnClosed(); // revert
        vm.Open();     // reopen

        Assert.Equal(16, vm.InputFontSize);
    }

    [Fact]
    public void PermissionPanel_HiddenWhenPlatformNeedsNoPermissions()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(false);
        permissions.Query().Returns(PermissionStatus.AllGranted);

        var sut = CreateSut(permissions);
        sut.RefreshPermissions();

        Assert.False(sut.ShowPermissionPanel);
    }

    [Fact]
    public void PermissionPanel_ShownWhenAccessibilityDenied()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(true);
        permissions.Query().Returns(new PermissionStatus(false, true, false));

        var sut = CreateSut(permissions);
        sut.RefreshPermissions();

        Assert.True(sut.ShowPermissionPanel);
        Assert.False(sut.AccessibilityGranted);
        Assert.True(sut.InputMonitoringGranted);
        Assert.Contains("Accessibility", sut.PermissionSummary);
    }

    [Fact]
    public void PermissionSummary_MentionsSecureInputWhenActive()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(true);
        permissions.Query().Returns(new PermissionStatus(true, true, true));

        var sut = CreateSut(permissions);
        sut.RefreshPermissions();

        Assert.True(sut.ShowPermissionPanel);
        Assert.Contains("Secure Keyboard Entry", sut.PermissionSummary);
    }

    [Fact]
    public void OpenAccessibilitySettings_DelegatesToPermissionService()
    {
        var permissions = Substitute.For<IPermissionService>();
        permissions.RequiresUserAction.Returns(true);
        permissions.Query().Returns(new PermissionStatus(false, false, false));

        var sut = CreateSut(permissions);
        sut.OpenAccessibilitySettingsCommand.Execute(null);

        permissions.Received(1).OpenAccessibilitySettings();
    }
}
