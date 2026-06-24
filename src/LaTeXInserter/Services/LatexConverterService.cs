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
    private FrozenSet<string> _hasArg;
    private List<string> _commandNames;

    private static readonly FrozenSet<string> IgnoreAsFallback = FrozenSet.ToFrozenSet(
    [
        "\\text", "\\mathrm", "\\mathbb", "\\mathbf", "\\mathbfit",
        "\\mathcal", "\\mathfrak", "\\mathsf", "\\mathsfbf", "\\mathsfbfit",
        "\\mathsfit", "\\mathtt", "\\left", "\\right", "\\not",
        "\\overleftrightarrow", "\\overline", "\\underbar", "\\underleftarrow",
        "\\underline", "\\underrightarrow"
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
    public IReadOnlyList<string> CommandNames => _commandNames;

    public LatexConverterService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _commands = LoadDefaultCommands();
        _hasArg = default!;
        _commandNames = default!;
        MergeCustomMappings();
    }

    public void Reload()
    {
        _commands = LoadDefaultCommands();
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
            return string.Empty;

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
                        sb.Append(cmd);
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
                    var result = HandleCmds([cmd], groupContent);

                    // If unresolved (returned raw "^{...}" or "_{...}"), retry with raw group text.
                    // This handles cases like ^{\gamma} where ParseGroup resolves \gamma→Unicode
                    // before HandleCmds can look up the combined key "^{\gamma}".
                    if (result == $"{cmd}{{{groupContent}}}")
                    {
                        var rawResult = HandleCmds([cmd], rawGroupContent);
                        if (rawResult != $"{cmd}{{{rawGroupContent}}}")
                            result = rawResult;
                    }

                    sb.Append(result);
                }
                else if (pos < span.Length)
                {
                    // Subscript/superscript of single char
                    var leaf = span[pos].ToString();
                    pos++;
                    var result = HandleCmds([cmd], leaf);
                    sb.Append(result);
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
                            sb.Append(cmd);
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
                    var result = HandleCmds([cmd], groupContent);

                    if (result == $"{cmd}{{{groupContent}}}")
                    {
                        var rawResult = HandleCmds([cmd], rawGroupContent);
                        if (rawResult != $"{cmd}{{{rawGroupContent}}}")
                            result = rawResult;
                    }

                    sb.Append(result);
                }
                else if (pos < span.Length)
                {
                    var leaf = span[pos].ToString();
                    pos++;
                    var result = HandleCmds([cmd], leaf);
                    sb.Append(result);
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

            // Step 2: resolve leaf if innermost (first pass)
            if (innermost && _commands.TryGetValue(leaf, out var leafResult))
            {
                leaf = leafResult;
            }

            // Step 3: pass-through commands
            if (cmd == "\\text" || cmd == "\\mathrm")
            {
                innermost = false;
                continue;
            }

            // Step 4: try cmd as modifier (e.g. \hat → combining circumflex)
            if (_commands.TryGetValue(cmd, out var cmdResult))
            {
                leaf = leaf + cmdResult;
                innermost = false;
                continue;
            }

            // Step 5: no mapping — return raw or skip if ignored
            if (!IgnoreAsFallback.Contains(cmd))
            {
                return $"{cmd}{{{leaf}}}";
            }

            innermost = false;
        }

        return leaf;
    }

    private static Dictionary<string, string> LoadDefaultCommands()
    {
        var assembly = typeof(LatexConverterService).Assembly;
        using var stream = assembly.GetManifestResourceStream("LaTeXInserter.Assets.Commands.json")!;
        return JsonSerializer.Deserialize(stream, JsonContext.Default.DictionaryStringString)!;
    }
}
