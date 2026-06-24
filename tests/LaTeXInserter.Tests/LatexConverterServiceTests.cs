using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;
using LaTeXInserter.Services;
using NSubstitute;
using Xunit;

namespace LaTeXInserter.Tests;

public class LatexConverterServiceTests
{
    private static LatexConverterService CreateService(IEnumerable<string>? customLines = null)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.GetCustomMappingLines().Returns(customLines ?? []);
        return new LatexConverterService(settings);
    }

    [Fact]
    public void SimpleCommand() => Assert.Equal("\U0001D6FC", CreateService().Convert(@"\alpha"));

    [Fact]
    public void PlainText() => Assert.Equal("hello", CreateService().Convert("hello"));

    [Fact]
    public void MixedTextAndCommand()
    {
        var result = CreateService().Convert(@"x = \alpha + \beta");
        Assert.Contains("\U0001D6FC", result); // math italic alpha
        Assert.Contains("\U0001D6FD", result); // math italic beta
    }

    [Fact]
    public void Superscript() => Assert.Equal("x²", CreateService().Convert("x^2"));

    [Fact]
    public void SuperscriptCommand() => Assert.Equal("xᵞ", CreateService().Convert(@"x^{\gamma}"));

    [Fact]
    public void SubscriptCommand() => Assert.Equal("xᵧ", CreateService().Convert(@"x_{\gamma}"));

    [Fact]
    public void Subscript()
    {
        // _{i} maps to ᵢ (U+1D62)
        var result = CreateService().Convert("x_i");
        Assert.Equal("xᵢ", result);
    }

    [Fact]
    public void CommandWithArgument()
    {
        // \hat{a}: a→math italic a, \hat→combining circumflex → 𝑎̂
        var result = CreateService().Convert(@"\hat{a}");
        // 𝑎 (U+1D44E) + combining circumflex accent (U+0302)
        Assert.Equal("\U0001D44Ê", result);
    }

    [Fact]
    public void NestedCommandWithArgument()
    {
        var result = CreateService().Convert(@"\vec{\alpha}");
        // Should not be raw LaTeX — vec combining arrow should be applied
        Assert.DoesNotContain(@"\vec", result);
        // Result should be just the alpha char + combining modifier (2+ chars)
        Assert.True(result.Length >= 2, $"Expected combined output, got: {result}");
        Assert.DoesNotContain("{", result);
    }

    [Fact]
    public void EscapedBraces() => Assert.Equal("{}", CreateService().Convert(@"\{\}"));

    [Fact]
    public void UnknownCommand() => Assert.Equal(@"\unknownfoo", CreateService().Convert(@"\unknownfoo"));

    [Fact]
    public void MalformedInputNoException()
    {
        var result = CreateService().Convert("x^{");
        Assert.NotNull(result);
    }

    [Fact]
    public void EmptyInput() => Assert.Equal("", CreateService().Convert(""));

    [Fact]
    public void TextCommandPassesThrough()
    {
        var result = CreateService().Convert(@"\text{hello}");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void CustomMappingOverride()
    {
        var svc = CreateService(["\\alpha \U0001D6FD"]); // override alpha to beta char
        var result = svc.Convert(@"\alpha");
        Assert.Equal("\U0001D6FD", result);
    }

    [Fact]
    public void CustomMappingWithBraceAutoHasArg()
    {
        // Combined entry: \myhat{x} directly in dict
        var svc = CreateService(["\\myhat{x} x_hat"]);
        var result = svc.Convert(@"\myhat{x}");
        Assert.Equal("x_hat", result);
    }

    [Fact]
    public void CommandsPropertyNotEmpty()
    {
        var svc = CreateService();
        Assert.NotEmpty(svc.Commands);
    }

    [Fact]
    public void CommandNamesStartWithBackslash()
    {
        var svc = CreateService();
        Assert.All(svc.CommandNames, name => Assert.StartsWith("\\", name));
    }
}
