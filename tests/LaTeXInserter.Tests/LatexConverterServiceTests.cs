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
        // \hat{a}: single letter in braces is NOT math-italicized;
        // \hat → combining circumflex (U+0302) applied as suffix over the base.
        // Result: plain 'a' + ̂ = â
        var result = CreateService().Convert(@"\hat{a}");
        Assert.Equal("â", result);
    }

    [Fact]
    public void BraceGroupPrefixSymbol_PlacesBeforeContent()
    {
        // \sqrt{x^2} → √x² : √ is a math symbol (prefix), not a combining mark,
        // so it is placed before the group content.
        Assert.Equal("√x²", CreateService().Convert(@"\sqrt{x^2}"));
    }

    [Fact]
    public void BraceGroupPrefixSymbol_SingleArg()
    {
        // \sqrt{x} → √x : prefix symbol places before single-char leaf too
        // (matches the space form \sqrt x → √x).
        Assert.Equal("√x", CreateService().Convert(@"\sqrt{x}"));
    }

    [Fact]
    public void BraceGroupCombiningMark_MultiChar_AttachesToFirstChar()
    {
        // \bar{x^2} → x̄² : combining macron (U+0304) attaches to the first
        // base char of the group and follows it — same render as x\bar^2 —
        // so it anchors correctly in every font (Calibri, Consolas, Times, Arial).
        Assert.Equal("x̄²", CreateService().Convert(@"\bar{x^2}"));
    }

    [Fact]
    public void BraceGroupCombiningMark_SingleChar_PlacesAfterBase()
    {
        // \overline{x} → x̄ : combining overline (U+0305) over a single base
        // letter keeps the suffix form; the base letter is NOT math-italicized.
        Assert.Equal("x̅", CreateService().Convert(@"\overline{x}"));
    }

    [Fact]
    public void BraceGroupCombiningSymbolModifierSymbol_SingleChar_Suffix()
    {
        // \vec{x} → x⃗ : \vec maps to U+20D7 (ModifierSymbol, not a Unicode
        // Mark category) but is still a combining glyph — it must attach after
        // the base. Guards the range-based combining check (category test would
        // wrongly prefix it as ⃗x).
        Assert.Equal("x⃗", CreateService().Convert(@"\vec{x}"));
    }

    [Fact]
    public void BraceGroupCombiningSymbolModifierSymbol_MultiChar_AttachesToFirstChar()
    {
        // \vec{x^2} → x⃗² : combining arrow on the first char, rest follows.
        Assert.Equal("x⃗²", CreateService().Convert(@"\vec{x^2}"));
    }

    [Fact]
    public void BraceGroupCombiningMark_SurrogateBase_Suffix()
    {
        // \bar{\alpha} → 𝛼̄ : leaf resolves to math-italic alpha (a UTF-16
        // surrogate pair); the mark must follow the full pair, not split it.
        Assert.Equal("\U0001D6FC̄", CreateService().Convert(@"\bar{\alpha}"));
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

    [Fact]
    public void UnresolvedCommand_Tracked()
    {
        var svc = CreateService();
        svc.Convert(@"\unknownfoo");
        Assert.NotEmpty(svc.LastUnresolvedCommands);
        Assert.Contains(@"\unknownfoo", svc.LastUnresolvedCommands);
    }

    [Fact]
    public void ResolvedCommand_NotTracked()
    {
        var svc = CreateService();
        svc.Convert(@"\alpha");
        Assert.Empty(svc.LastUnresolvedCommands);
    }

    [Fact]
    public void SubscriptGroupNoGlyph_NotDoubleReported()
    {
        var svc = CreateService();
        var result = svc.Convert("_{bad}");
        Assert.Equal("bₐd", result);
        // exactly one entry, not two
        Assert.Equal(1, svc.LastUnresolvedCommands.Count(x => x == "_{bad}"));
    }

    [Fact]
    public void SubscriptGroup()
    {
        // _{test}: t,e,s,t all have subscript glyphs -> ₜₑₛₜ
        Assert.Equal("ₜₑₛₜ", CreateService().Convert("_{test}"));
    }

    [Fact]
    public void SuperscriptGroup()
    {
        // ^{n2}: n->ⁿ, 2->² -> ⁿ²
        Assert.Equal("ⁿ²", CreateService().Convert("^{n2}"));
    }

    [Fact]
    public void SubscriptGroupPartialGlyph()
    {
        // _{bad}: b miss, a->ₐ, d miss -> bₐd; records _{bad} in unresolved
        var svc = CreateService();
        Assert.Equal("bₐd", svc.Convert("_{bad}"));
        Assert.Contains("_{bad}", svc.LastUnresolvedCommands);
    }

    [Fact]
    public void SubscriptSingleCharNoGlyph()
    {
        // q has no subscript glyph: strip braces -> xq, record _{q}
        var svc = CreateService();
        Assert.Equal("xq", svc.Convert("x_q"));
        Assert.Contains("_{q}", svc.LastUnresolvedCommands);
    }

    [Fact]
    public void SuperscriptSingleCharNoGlyph()
    {
        // S has no superscript glyph (uppercase S absent): strip braces -> xS,
        // record ^{S}
        var svc = CreateService();
        Assert.Equal("xS", svc.Convert("x^S"));
        Assert.Contains("^{S}", svc.LastUnresolvedCommands);
    }

    [Fact]
    public void SubscriptCapital_StaysPlain_NotFoldedToLowercase()
    {
        // Unicode has no subscript capitals; the map must not fold _A -> ₐ
        // (lowercase subscript a). It stays plain A, like a missing glyph.
        var svc = CreateService();
        Assert.Equal("A", svc.Convert("_A"));
        Assert.Contains("_{A}", svc.LastUnresolvedCommands);
    }

    [Fact]
    public void SubscriptGroupCapitalMixed_StaysPlainForCapitals()
    {
        // _{Ab}: A plain (no cap glyph), b plain (no subscript b glyph either);
        // capitals must not fold to their lowercase subscript glyphs.
        var svc = CreateService();
        Assert.Equal("Ab", svc.Convert("_{Ab}"));
    }
}
