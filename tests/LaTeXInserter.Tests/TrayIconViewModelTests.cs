using System;
using Avalonia.Controls;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.ViewModels;
using NSubstitute;
using SharpHook.Native;
using Xunit;

namespace LaTeXInserter.Tests;

public class TrayIconViewModelTests
{
    private readonly IHotkeyService _hotkeyService;
    private readonly ISettingsService _settingsService;

    public TrayIconViewModelTests()
    {
        _hotkeyService = Substitute.For<IHotkeyService>();
        _hotkeyService.CurrentHotkey.Returns(new HotkeyChord(ModifierMask.Control | ModifierMask.Alt, SharpHook.Data.KeyCode.VcM));
        _settingsService = Substitute.For<ISettingsService>();
    }

    [Fact]
    public void MenuItems_LabelsHaveNoTrailingEllipsis()
    {
        var vm = new TrayIconViewModel(_hotkeyService, _settingsService);
        var menu = vm.TrayMenu;

        Assert.Equal(8, menu.Items.Count);

        var showHideItem = Assert.IsType<NativeMenuItem>(menu.Items[0]);
        Assert.StartsWith("Show/Hide Overlay", showHideItem.Header);

        Assert.IsType<NativeMenuItemSeparator>(menu.Items[1]);

        var settingsItem = Assert.IsType<NativeMenuItem>(menu.Items[2]);
        Assert.Equal("Settings", settingsItem.Header);
        Assert.DoesNotContain("...", settingsItem.Header);

        var editMappingsItem = Assert.IsType<NativeMenuItem>(menu.Items[3]);
        Assert.Equal("Edit Custom Mappings", editMappingsItem.Header);
        Assert.DoesNotContain("...", editMappingsItem.Header);

        Assert.IsType<NativeMenuItemSeparator>(menu.Items[4]);

        var checkForUpdatesItem = Assert.IsType<NativeMenuItem>(menu.Items[5]);
        Assert.Equal("Check for Updates", checkForUpdatesItem.Header);
        Assert.DoesNotContain("...", checkForUpdatesItem.Header);

        Assert.IsType<NativeMenuItemSeparator>(menu.Items[6]);

        var quitItem = Assert.IsType<NativeMenuItem>(menu.Items[7]);
        Assert.Equal("Quit", quitItem.Header);
    }

    [Fact]
    public void MenuItems_CommandsTriggerEvents()
    {
        var vm = new TrayIconViewModel(_hotkeyService, _settingsService);

        bool showHideFired = false;
        bool settingsFired = false;
        bool editMappingsFired = false;
        bool updateFired = false;
        bool quitFired = false;

        vm.ShowOverlayRequested += (_, _) => showHideFired = true;
        vm.SettingsRequested += (_, _) => settingsFired = true;
        vm.EditMappingsRequested += (_, _) => editMappingsFired = true;
        vm.CheckForUpdatesRequested += (_, _) => updateFired = true;
        vm.QuitRequested += (_, _) => quitFired = true;

        vm.ShowHideOverlayCommand.Execute(null);
        Assert.True(showHideFired);

        vm.SettingsCommand.Execute(null);
        Assert.True(settingsFired);

        vm.EditMappingsCommand.Execute(null);
        Assert.True(editMappingsFired);

        vm.CheckForUpdatesCommand.Execute(null);
        Assert.True(updateFired);

        vm.QuitCommand.Execute(null);
        Assert.True(quitFired);
    }
}
