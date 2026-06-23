using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.ViewModels;
using NSubstitute;
using Xunit;

namespace LaTeXInserter.Tests;

public class OverlayViewModelTests
{
    private static ILatexConverterService CreateConverter(
        string? convertResult = null,
        IReadOnlyList<string>? commandNames = null,
        IReadOnlyDictionary<string, string>? commands = null)
    {
        var mock = Substitute.For<ILatexConverterService>();
        mock.Convert(Arg.Any<string>()).Returns(convertResult ?? string.Empty);
        mock.CommandNames.Returns(commandNames ?? []);
        mock.Commands.Returns(commands ?? new Dictionary<string, string>());
        return mock;
    }

    private static ISettingsService CreateSettings(AppSettings? settings = null)
    {
        var mock = Substitute.For<ISettingsService>();
        mock.Load().Returns(settings ?? AppSettings.Default);
        return mock;
    }

    [Fact]
    public void EmptyInput_NoPreviewNoAutocomplete()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "";

        Assert.Equal(string.Empty, vm.PreviewText);
        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void PlainText_PreviewsConverted_NoAutocomplete()
    {
        var converter = CreateConverter(convertResult: "hello", commandNames: []);
        converter.Convert("hello").Returns("hello");
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "hello";

        Assert.Equal("hello", vm.PreviewText);
        Assert.False(vm.IsAutocompleteOpen);
        converter.Received().Convert("hello");
    }

    [Fact]
    public void PartialCommand_OpensAutocomplete()
    {
        var names = new List<string> { "\\alpha", "\\approx", "\\beta" };
        var commands = new Dictionary<string, string> { { "\\alpha", "α" }, { "\\approx", "≈" }, { "\\beta", "β" } };
        var converter = CreateConverter(commandNames: names, commands: commands);
        converter.Convert("\\alp").Returns("αp");
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "\\alp";

        Assert.True(vm.IsAutocompleteOpen);
        Assert.Equal(1, vm.AutocompleteItems.Count);
        Assert.Equal("\\alpha", vm.AutocompleteItems[0].Command);
        Assert.Equal("α", vm.AutocompleteItems[0].Unicode);
        Assert.NotNull(vm.SelectedAutocompleteItem);
        Assert.Equal("\\alpha", vm.SelectedAutocompleteItem.Command);
    }

    [Fact]
    public void CompleteCommandWithSpace_ClosesAutocomplete()
    {
        var names = new List<string> { "\\alpha" };
        var converter = CreateConverter(commandNames: names);
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "\\alpha ";

        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void CommitAutocomplete_ReplacesOnlyTrailingPrefix()
    {
        var names = new List<string> { "\\alpha" };
        var commands = new Dictionary<string, string> { { "\\alpha", "α" } };
        var converter = CreateConverter(commandNames: names, commands: commands);
        converter.Convert("x = \\alp + \\alp").Returns("x = αp + αp");
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "x = \\alp + \\alp";
        var item = new AutocompleteItem("\\alpha", "α");
        vm.CommitAutocomplete(item);

        // Only the trailing \alp should be replaced, not the first one
        Assert.Equal("x = \\alp + \\alpha", vm.InputText);
    }

    [Fact]
    public void CommitAutocomplete_SuppressesRefilter()
    {
        var names = new List<string> { "\\alpha" };
        var commands = new Dictionary<string, string> { { "\\alpha", "α" } };
        var converter = CreateConverter(commandNames: names, commands: commands);
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "\\alp";
        Assert.True(vm.IsAutocompleteOpen);

        var item = new AutocompleteItem("\\alpha", "α");
        vm.CommitAutocomplete(item);

        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void NavigateAutocomplete_IncrementAndClamp()
    {
        var names = new List<string> { "\\alpha", "\\approx", "\\angle" };
        var commands = new Dictionary<string, string> { { "\\alpha", "α" }, { "\\approx", "≈" }, { "\\angle", "∠" } };
        var converter = CreateConverter(commandNames: names, commands: commands);
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "\\a";
        Assert.Equal(3, vm.AutocompleteItems.Count);
        Assert.NotNull(vm.SelectedAutocompleteItem);
        Assert.Equal("\\alpha", vm.SelectedAutocompleteItem.Command);

        vm.NavigateAutocomplete(1);
        Assert.Equal("\\approx", vm.SelectedAutocompleteItem.Command);

        vm.NavigateAutocomplete(1);
        Assert.Equal("\\angle", vm.SelectedAutocompleteItem.Command);

        // Clamp at upper bound
        vm.NavigateAutocomplete(1);
        Assert.Equal("\\angle", vm.SelectedAutocompleteItem.Command);

        // Clamp at lower bound
        vm.NavigateAutocomplete(-3);
        Assert.Equal("\\alpha", vm.SelectedAutocompleteItem.Command);
    }

    [Fact]
    public void Cancel_ResetsStateAndFiresHideRequested()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter, CreateSettings());
        var hideFired = false;
        vm.HideRequested += (_, _) => hideFired = true;

        vm.InputText = "\\alpha";
        vm.Cancel();

        Assert.Equal(string.Empty, vm.InputText);
        Assert.False(vm.IsAutocompleteOpen);
        Assert.True(hideFired);
    }

    [Fact]
    public void ResetState_ClearsAll()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "\\alpha";
        vm.ResetState();

        Assert.Equal(string.Empty, vm.InputText);
        Assert.Equal(string.Empty, vm.PreviewText);
        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void Submit_FiresWithConvertedText()
    {
        var converter = CreateConverter();
        converter.Convert("\\alpha").Returns("α");
        var vm = new OverlayViewModel(converter, CreateSettings());
        string? submitted = null;
        vm.SubmitRequested += (_, text) => submitted = text;

        vm.InputText = "\\alpha";
        vm.Submit();

        Assert.Equal("α", submitted);
    }

    [Fact]
    public void GetSelectedAutocompleteCommand_ReturnsCurrent()
    {
        var names = new List<string> { "\\alpha", "\\angle" };
        var commands = new Dictionary<string, string> { { "\\alpha", "α" }, { "\\angle", "∠" } };
        var converter = CreateConverter(commandNames: names, commands: commands);
        var vm = new OverlayViewModel(converter, CreateSettings());

        vm.InputText = "\\a";
        vm.NavigateAutocomplete(1);

        Assert.Equal("\\angle", vm.GetSelectedAutocompleteCommand());
    }

    [Fact]
    public void GetSelectedAutocompleteCommand_NoSelection_ReturnsNull()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter, CreateSettings());

        Assert.Null(vm.GetSelectedAutocompleteCommand());
    }
}
