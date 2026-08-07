using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using System.Text.Json;
using LaTeXInserter.Abstractions;
using LaTeXInserter.Models;

namespace LaTeXInserter.Services;

public sealed class LatexConverterService : ILatexConverterService
{
    private readonly ISettingsService _settingsService;
    private Dictionary<string, string> _commands;
    private Dictionary<string, string> _defaultCommands;
    private FrozenSet<string> _hasArg;
    private List<string> _commandNames;
    private readonly List<string> _unresolvedCommands = [];

    private static readonly FrozenSet<string> IgnoreAsFallback = FrozenSet.ToFrozenSet(
    [
        "\\text", "\\mathrm", "\\mathbb", "\\mathbf", "\\mathbfit",
        "\\mathcal", "\\mathfrak", "\\mathsf", "\\mathsfbf", "\\mathsfbfit",
        "\\mathsfit", "\\mathtt", "\\left", "\\right", "\\not",
        "\\overleftrightarrow", "\\overline", "\\underbar", "\\underleftarrow",
        "\\underline", "\\underrightarrow", "^", "_"
    ]);

    private static readonly Dictionary<string, string> Escaped = new()
    {
        ["\\\\"] = "\\",
        ["\\#"] = "#",
        ["\\%"] = "%",
        ["\\&"] = "&",
        ["\\{"] = "{",
        ["\\}"] = "}",
        ["\\_"] = "_",
        ["\\,"] = " ", // thin space
    };

    private static readonly FrozenSet<string> DefaultHasArg = FrozenSet.ToFrozenSet(
    [
        "\\Big", "\\Bigg", "\\LVec", "\\acute", "\\bar", "\\big", "\\breve",
        "\\check", "\\ddddot", "\\dddot", "\\ddot", "\\dot", "\\grave", "\\hat",
        "\\left", "\\lvec", "\\mathbb", "\\mathbf", "\\mathbfit", "\\mathcal",
        "\\mathfrak", "\\mathring", "\\mathrm", "\\mathsf", "\\mathsfbf",
        "\\mathsfbfit", "\\mathsfit", "\\mathtt", "\\not", "\\overleftrightarrow",
        "\\overline", "\\right", "\\slash", "\\spddot", "\\sqrt", "\\text",
        "\\tilde", "\\underbar", "\\underleftarrow", "\\underline",
        "\\underrightarrow", "\\utilde", "\\vec", "^", "_"
    ]);

    public IReadOnlyDictionary<string, string> Commands => _commands;
    public IReadOnlyDictionary<string, string> DefaultCommands => _defaultCommands;
    public IReadOnlyList<string> CommandNames => _commandNames;
    public IReadOnlyList<string> LastUnresolvedCommands => _unresolvedCommands;

    public LatexConverterService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _commands = LoadDefaultCommands();
        _defaultCommands = new Dictionary<string, string>(_commands);
        _hasArg = default!;
        _commandNames = default!;
        MergeCustomMappings();
    }

    public void Reload()
    {
        _commands = LoadDefaultCommands();
        _defaultCommands = new Dictionary<string, string>(_commands);
        MergeCustomMappings();
    }

    private void MergeCustomMappings()
    {
        var customLines = _settingsService.GetCustomMappingLines().ToList();
        var customHasArg = new HashSet<string>();
        foreach (var line in customLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#')) continue;

            var spaceIdx = trimmed.IndexOf(' ');
            if (spaceIdx < 0) continue;

            var cmd = trimmed[..spaceIdx];
            var unicode = trimmed[(spaceIdx + 1)..];
            _commands[cmd] = unicode;

            if (cmd.Contains('{'))
            {
                var braceIdx = cmd.IndexOf('{');
                customHasArg.Add(cmd[..braceIdx]);
            }
        }

        var allHasArg = new HashSet<string>(DefaultHasArg);
        allHasArg.UnionWith(customHasArg);
        _hasArg = allHasArg.ToFrozenSet();

        _commandNames = _commands.Keys
            .Where(k => k.StartsWith('\\'))
            .OrderBy(k => k)
            .ToList();
    }

    private const int MaxNestingDepth = 30;

    public string Convert(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            _unresolvedCommands.Clear();
            return string.Empty;
        }

        _unresolvedCommands.Clear();
        var sb = new StringBuilder(input.Length);
        var pos = 0;
        var span = input.AsSpan();
        ParseMath(span, ref pos, sb, depth: 0);
        return sb.ToString();
    }

    private void ParseMath(ReadOnlySpan<char> span, ref int pos, StringBuilder sb, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            sb.Append(span[pos..].ToString());
            pos = span.Length;
            return;
        }

        var safety = 0;
        while (pos < span.Length)
        {
            if (++safety > span.Length) break;
            var ch = span[pos];

            if (ch == '\\')
            {
                // Check escaped sequences first
                var escaped = TryEscaped(span, ref pos);
                if (escaped is not null)
                {
                    sb.Append(escaped);
                    continue;
                }

                // Command
                var cmd = ParseCommand(span, ref pos);
                if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
                {
                    var groupContent = ParseGroup(span, ref pos, depth + 1);
                    var result = HandleCmds([cmd], groupContent);
                    sb.Append(result);
                }
                else
                {
                    // Standalone command, no argument consumed
                    if (_commands.TryGetValue(cmd, out var mapped))
                        sb.Append(mapped);
                    else
                    {
                        _unresolvedCommands.Add(cmd);
                        sb.Append(cmd);
                    }
                }
            }
            else if (ch == '{')
            {
                var groupContent = ParseGroup(span, ref pos, depth + 1);
                sb.Append(groupContent);
            }
            else if (ch == '}' || ch == '$')
            {
                // End of group or math mode
                pos++;
                return;
            }
            else if (ch == '_' || ch == '^')
            {
                pos++;
                var cmd = ch.ToString();
                if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
                {
                    var openBrace = pos; // save position of '{'
                    var rawGroupContent = CaptureRawGroup(span, openBrace);
                    var groupContent = ParseGroup(span, ref pos, depth + 1);

                    // Precedence (highest -> lowest):
                    //  P1: combined key on resolved content (custom override e.g. ^{foo}).
                    //  P2: combined key on raw content (e.g. ^{\gamma} -> ᵞ, _{\gamma} -> ᵧ).
                    //  P3: per-char best-effort fallback (_{test} -> ₜₑₛₜ, ^{n2} -> ⁿ²).
                    //  P4: missing-glyph chars kept as plain; raw form recorded for the hint.
                    if (_commands.TryGetValue($"{cmd}{{{groupContent}}}", out var resolvedHit))
                        sb.Append(resolvedHit);
                    else if (_commands.TryGetValue($"{cmd}{{{rawGroupContent}}}", out var rawHit))
                        sb.Append(rawHit);
                    else
                    {
                        var (fb, miss) = ConvertSubSupChars(ch, groupContent);
                        sb.Append(fb);
                        if (miss)
                            _unresolvedCommands.Add($"{cmd}{{{rawGroupContent}}}");
                    }
                }
                else if (pos < span.Length)
                {
                    // Subscript/superscript of single char
                    var leaf = span[pos].ToString();
                    pos++;
                    if (_commands.TryGetValue($"{cmd}{{{leaf}}}", out var glyphHit))
                        sb.Append(glyphHit);
                    else
                    {
                        // No single-char glyph: strip braces, keep plain char
                        // (consistent with the multi-char best-effort rule).
                        sb.Append(leaf);
                        _unresolvedCommands.Add($"{cmd}{{{leaf}}}");
                    }
                }
                else
                {
                    sb.Append(cmd);
                }
            }
            else
            {
                sb.Append(ch);
                pos++;
            }
        }
    }

    private string? TryEscaped(ReadOnlySpan<char> span, ref int pos)
    {
        foreach (var kvp in Escaped)
        {
            if (span.Length - pos >= kvp.Key.Length &&
                span.Slice(pos, kvp.Key.Length).SequenceEqual(kvp.Key.AsSpan()))
            {
                pos += kvp.Key.Length;
                return kvp.Value;
            }
        }
        return null;
    }

    private static string ParseCommand(ReadOnlySpan<char> span, ref int pos)
    {
        pos++; // skip '\'
        var start = pos;
        var safety = 0;

        while (pos < span.Length && char.IsLetter(span[pos]))
        {
            if (++safety > span.Length) break;
            pos++;
        }

        // Strip optional trailing whitespace
        safety = 0;
        while (pos < span.Length && span[pos] == ' ')
        {
            if (++safety > span.Length) break;
            pos++;
        }

        return $"\\{span[start..pos].ToString()}".TrimEnd();
    }

    private string ParseGroup(ReadOnlySpan<char> span, ref int pos, int depth)
    {
        if (depth > MaxNestingDepth)
        {
            if (pos < span.Length && span[pos] == '{') pos++;
            return span[pos..].ToString();
        }

        pos++; // skip '{'
        var sb = new StringBuilder();
        var braceDepth = 1;

        var safety = 0;
        while (pos < span.Length && braceDepth > 0)
        {
            if (++safety > span.Length) break;
            var ch = span[pos];

            if (ch == '{')
            {
                braceDepth++;
                sb.Append(ch);
                pos++;
            }
            else if (ch == '}')
            {
                braceDepth--;
                if (braceDepth == 0)
                {
                    pos++; // skip closing '}'
                    break;
                }
                sb.Append(ch);
                pos++;
            }
            else if (ch == '\\' && pos + 1 < span.Length)
            {
                var escaped = TryEscaped(span, ref pos);
                if (escaped is not null)
                {
                    sb.Append(escaped);
                }
                else
                {
                    var cmd = ParseCommand(span, ref pos);
                    if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
                    {
                        var groupContent = ParseGroup(span, ref pos, depth + 1);
                        var result = HandleCmds([cmd], groupContent);
                        sb.Append(result);
                    }
                    else
                    {
                        if (_commands.TryGetValue(cmd, out var mapped))
                            sb.Append(mapped);
                        else
                        {
                            _unresolvedCommands.Add(cmd);
                            sb.Append(cmd);
                        }
                    }
                }
            }
            else if (ch == '_' || ch == '^')
            {
                pos++;
                var cmd = ch.ToString();
                if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
                {
                    var openBrace = pos;
                    var rawGroupContent = CaptureRawGroup(span, openBrace);
                    var groupContent = ParseGroup(span, ref pos, depth + 1);

                    // Precedence (highest -> lowest):
                    //  P1: combined key on resolved content (custom override e.g. ^{foo}).
                    //  P2: combined key on raw content (e.g. ^{\gamma} -> ᵞ, _{\gamma} -> ᵧ).
                    //  P3: per-char best-effort fallback (_{test} -> ₜₑₛₜ, ^{n2} -> ⁿ²).
                    //  P4: missing-glyph chars kept as plain; raw form recorded for the hint.
                    if (_commands.TryGetValue($"{cmd}{{{groupContent}}}", out var resolvedHit))
                        sb.Append(resolvedHit);
                    else if (_commands.TryGetValue($"{cmd}{{{rawGroupContent}}}", out var rawHit))
                        sb.Append(rawHit);
                    else
                    {
                        var (fb, miss) = ConvertSubSupChars(ch, groupContent);
                        sb.Append(fb);
                        if (miss)
                            _unresolvedCommands.Add($"{cmd}{{{rawGroupContent}}}");
                    }
                }
                else if (pos < span.Length)
                {
                    // Subscript/superscript of single char
                    var leaf = span[pos].ToString();
                    pos++;
                    if (_commands.TryGetValue($"{cmd}{{{leaf}}}", out var glyphHit))
                        sb.Append(glyphHit);
                    else
                    {
                        // No single-char glyph: strip braces, keep plain char
                        // (consistent with the multi-char best-effort rule).
                        sb.Append(leaf);
                        _unresolvedCommands.Add($"{cmd}{{{leaf}}}");
                    }
                }
                else
                {
                    sb.Append(cmd);
                }
            }
            else
            {
                sb.Append(ch);
                pos++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Captures the raw text between braces at <paramref name="openBracePos"/>
    /// without resolving any LaTeX commands inside. Does NOT advance <c>pos</c>.
    /// </summary>
    private static string CaptureRawGroup(ReadOnlySpan<char> span, int openBracePos)
    {
        var depth = 1;
        var pos = openBracePos + 1;
        while (pos < span.Length && depth > 0)
        {
            if (span[pos] == '{') depth++;
            else if (span[pos] == '}') depth--;
            pos++;
        }
        // If unmatched brace, depth>0 and pos==span.Length; return everything after '{'
        var end = depth > 0 ? pos : pos - 1;
        if (end <= openBracePos + 1)
            return string.Empty;
        return span[(openBracePos + 1)..end].ToString();
    }

    private string HandleCmds(List<string> cmds, string leaf)
    {
        if (cmds.Count == 0)
            return _commands.TryGetValue(leaf, out var v) ? v : leaf;

        var innermost = true;

        for (var i = cmds.Count - 1; i >= 0; i--)
        {
            var cmd = cmds[i];
            var combined = $"{cmd}{{{leaf}}}";

            // Step 1: try combined lookup first (e.g. \hat{a} → â)
            if (_commands.TryGetValue(combined, out var combinedResult))
            {
                leaf = combinedResult;
                innermost = false;
                continue;
            }

            // Step 2: resolve leaf if innermost (first pass).
            // A single ASCII letter is NOT resolved to its math-italic form
            // (e.g. x → 𝑥) — letters inside braces stay plain. Backslash
            // commands (\alpha) and other tokens still resolve here.
            if (innermost && !IsSingleAsciiLetter(leaf) &&
                _commands.TryGetValue(leaf, out var leafResult))
            {
                leaf = leafResult;
            }

            // Step 3: pass-through commands
            if (cmd == "\\text" || cmd == "\\mathrm")
            {
                innermost = false;
                continue;
            }

            // Step 4: try cmd as modifier (e.g. \hat → combining circumflex,
            // \sqrt → √). A combining mark attaches to a base char and renders
            // consistently only when it follows that base — so it is placed
            // after the first char of the leaf: a single-char leaf gets
            // x̄ / â / 𝛼⃗, a multi-char leaf gets x̄² / âb (mark on the first
            // char, rest follows — matches x\hat^2). A non-combining glyph
            // (√ and similar symbols) prefixes the whole leaf.
            if (_commands.TryGetValue(cmd, out var cmdResult))
            {
                leaf = ApplyModifier(cmdResult, leaf);
                innermost = false;
                continue;
            }

            // Step 5: no mapping — track as unresolved and return raw
            if (!IgnoreAsFallback.Contains(cmd))
            {
                var unresolved = $"{cmd}{{{leaf}}}";
                _unresolvedCommands.Add(unresolved);
                return unresolved;
            }

            innermost = false;
        }

        return leaf;
    }

    /// <summary>
    /// True iff <paramref name="leaf"/> is exactly one ASCII letter (a-z, A-Z).
    /// Used to keep plain letters inside braces from being resolved to their
    /// math-italic Unicode equivalents (x → 𝑥), which is undesirable for
    /// constructs like <c>\overline{x}</c>.
    /// </summary>
    private static bool IsSingleAsciiLetter(string leaf)
    {
        if (leaf.Length != 1) return false;
        var c = leaf[0];
        return (uint)(c - 'a') < 26u || (uint)(c - 'A') < 26u;
    }

    /// <summary>
    /// Composes a modifier glyph with its leaf according to the glyph's kind.
    /// Combining marks (diacriticals, combining arrows/overlines — including
    /// those categorized as ModifierSymbol like <c>\vec</c> U+20D7) attach to
    /// the first base char and follow it, so they render with a real anchor in
    /// every font: single-char leaf → <c>x̄</c>/<c>â</c>/<c>𝛼⃗</c>, multi-char
    /// leaf → <c>x̄²</c>/<c>âb</c> (mark on the first char, rest follows,
    /// matching <c>x\bar^2</c>). A non-combining glyph (e.g. <c>\sqrt</c> → √)
    /// prefixes the whole leaf (<c>√x²</c>).
    /// </summary>
    private static string ApplyModifier(string modifier, string leaf)
    {
        if (leaf.Length == 0) return modifier;
        if (!IsCombiningGlyph(modifier)) return modifier + leaf;

        // First base char's length, accounting for a UTF-16 surrogate pair
        // (e.g. math-italic 𝛼 = two chars): the mark must follow the full pair.
        var firstLen = leaf.Length >= 2 && char.IsSurrogatePair(leaf[0], leaf[1]) ? 2 : 1;

        // Single base char → suffix (x̄, â, 𝛼⃗).
        if (leaf.Length == firstLen) return leaf + modifier;

        // Multi-char → mark after the first base char (x̄², âb).
        return string.Concat(leaf.AsSpan(0, firstLen), modifier, leaf.AsSpan(firstLen));
    }

    /// <summary>
    /// True iff every char of <paramref name="glyph"/> is a combining mark,
    /// tested by codepoint range (U+0300–U+036F, U+1DC0–U+1DFF,
    /// U+20D0–U+20FF, U+FE20–U+FE2F). Range — not <see cref="char.GetUnicodeCategory"/>
    /// — is used because "combining diacritical marks for symbols" (U+20D0–,
    /// e.g. <c>\vec</c> U+20D7) are category <c>ModifierSymbol</c>, not Mark,
    /// yet still attach to a preceding base char.
    /// </summary>
    private static bool IsCombiningGlyph(string glyph)
    {
        if (glyph.Length == 0) return false;
        foreach (var ch in glyph)
        {
            uint c = ch;
            if (!(c is >= 0x0300 and <= 0x036F
                  || c is >= 0x1DC0 and <= 0x1DFF
                  || c is >= 0x20D0 and <= 0x20FF
                  || c is >= 0xFE20 and <= 0xFE2F))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Best-effort per-character fallback for "_" or "^" over a group whose
    /// combined lookups (resolved + raw) both missed. For each char in
    /// <paramref name="content"/> looks up the existing single-char key
    /// "{cmd}{{{c}}}" in <c>_commands</c> and appends the glyph when present;
    /// otherwise appends the plain char. <paramref name="hadMiss"/> is true iff
    /// any char had no glyph, so the caller can record the unresolved hint.
    /// </summary>
    private (string Result, bool HadMiss) ConvertSubSupChars(char cmd, string content)
    {
        var sb = new StringBuilder(content.Length);
        var hadMiss = false;
        foreach (var c in content)
        {
            if (_commands.TryGetValue($"{cmd}{{{c}}}", out var glyph))
                sb.Append(glyph);
            else
            {
                sb.Append(c);
                hadMiss = true;
            }
        }
        return (sb.ToString(), hadMiss);
    }

    private static Dictionary<string, string> LoadDefaultCommands()
    {
        var assembly = typeof(LatexConverterService).Assembly;
        using var stream = assembly.GetManifestResourceStream("LaTeXInserter.Assets.Commands.json")!;
        return JsonSerializer.Deserialize(stream, JsonContext.Default.DictionaryStringString)!;
    }
}
