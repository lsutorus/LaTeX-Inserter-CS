using LaTeXInserter.Abstractions;
using LaTeXInserter.ViewModels;
using NSubstitute;
using Xunit;

namespace LaTeXInserter.Tests;

public class OverlayViewModelTests
{
    private static ILatexConverterService CreateConverter(
        string? convertResult = null,
        IReadOnlyList<string>? commandNames = null)
    {
        var mock = Substitute.For<ILatexConverterService>();
        mock.Convert(Arg.Any<string>()).Returns(convertResult ?? string.Empty);
        mock.CommandNames.Returns(commandNames ?? []);
        return mock;
    }

    [Fact]
    public void EmptyInput_NoPreviewNoAutocomplete()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter);

        vm.InputText = "";

        Assert.Equal(string.Empty, vm.PreviewText);
        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void PlainText_PreviewsConverted_NoAutocomplete()
    {
        var converter = CreateConverter(convertResult: "hello", commandNames: []);
        converter.Convert("hello").Returns("hello");
        var vm = new OverlayViewModel(converter);

        vm.InputText = "hello";

        Assert.Equal("hello", vm.PreviewText);
        Assert.False(vm.IsAutocompleteOpen);
        converter.Received().Convert("hello");
    }

    [Fact]
    public void PartialCommand_OpensAutocomplete()
    {
        var names = new List<string> { "\\alpha", "\\approx", "\\beta" };
        var converter = CreateConverter(commandNames: names);
        converter.Convert("\\alp").Returns("αp");
        var vm = new OverlayViewModel(converter);

        vm.InputText = "\\alp";

        Assert.True(vm.IsAutocompleteOpen);
        Assert.Equal(1, vm.AutocompleteItems.Count);
        Assert.Equal("\\alpha", vm.AutocompleteItems[0]);
        Assert.Equal(0, vm.AutocompleteSelectedIndex);
    }

    [Fact]
    public void CompleteCommandWithSpace_ClosesAutocomplete()
    {
        var names = new List<string> { "\\alpha" };
        var converter = CreateConverter(commandNames: names);
        var vm = new OverlayViewModel(converter);

        vm.InputText = "\\alpha ";

        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void CommitAutocomplete_ReplacesOnlyTrailingPrefix()
    {
        var names = new List<string> { "\\alpha" };
        var converter = CreateConverter(commandNames: names);
        converter.Convert("x = \\alp + \\alp").Returns("x = αp + αp");
        var vm = new OverlayViewModel(converter);

        vm.InputText = "x = \\alp + \\alp";
        vm.CommitAutocomplete("\\alpha");

        // Only the trailing \alp should be replaced, not the first one
        Assert.Equal("x = \\alp + \\alpha", vm.InputText);
    }

    [Fact]
    public void CommitAutocomplete_SuppressesRefilter()
    {
        var names = new List<string> { "\\alpha" };
        var converter = CreateConverter(commandNames: names);
        var vm = new OverlayViewModel(converter);

        vm.InputText = "\\alp";
        Assert.True(vm.IsAutocompleteOpen);

        vm.CommitAutocomplete("\\alpha");

        Assert.False(vm.IsAutocompleteOpen);
    }

    [Fact]
    public void NavigateAutocomplete_IncrementAndClamp()
    {
        var names = new List<string> { "\\alpha", "\\approx", "\\angle" };
        var converter = CreateConverter(commandNames: names);
        var vm = new OverlayViewModel(converter);

        vm.InputText = "\\a";
        Assert.Equal(3, vm.AutocompleteItems.Count);
        Assert.Equal(0, vm.AutocompleteSelectedIndex);

        vm.NavigateAutocomplete(1);
        Assert.Equal(1, vm.AutocompleteSelectedIndex);

        vm.NavigateAutocomplete(1);
        Assert.Equal(2, vm.AutocompleteSelectedIndex);

        // Clamp at upper bound
        vm.NavigateAutocomplete(1);
        Assert.Equal(2, vm.AutocompleteSelectedIndex);

        // Clamp at lower bound
        vm.NavigateAutocomplete(-3);
        Assert.Equal(0, vm.AutocompleteSelectedIndex);
    }

    [Fact]
    public void Cancel_ResetsStateAndFiresHideRequested()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter);
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
        var vm = new OverlayViewModel(converter);

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
        var vm = new OverlayViewModel(converter);
        string? submitted = null;
        vm.SubmitRequested += (_, text) => submitted = text;

        vm.InputText = "\\alpha";
        vm.Submit();

        Assert.Equal("α", submitted);
    }

    [Fact]
    public void GetSelectedAutocompleteItem_ReturnsCurrent()
    {
        var names = new List<string> { "\\alpha", "\\angle" };
        var converter = CreateConverter(commandNames: names);
        var vm = new OverlayViewModel(converter);

        vm.InputText = "\\a";
        vm.NavigateAutocomplete(1);

        Assert.Equal("\\angle", vm.GetSelectedAutocompleteItem());
    }

    [Fact]
    public void GetSelectedAutocompleteItem_NoSelection_ReturnsNull()
    {
        var converter = CreateConverter();
        var vm = new OverlayViewModel(converter);

        Assert.Null(vm.GetSelectedAutocompleteItem());
    }
}
