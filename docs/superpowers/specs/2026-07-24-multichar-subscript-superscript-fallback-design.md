# Multi-character subscript/superscript conversion

Date: 2026-07-24
Status: Approved (design), pending implementation

## Problem

The LaTeX converter only recognizes subscript/superscript forms that exist as
single combined keys in `Commands.json`, namely:

- Single-char forms: `x^2` (via `^{2}` → `²`), `x_i` (via `_i` / `_{i}`).
- Resolved-command forms: `x^{\gamma}` (combined key `^{\gamma}` → `ᵞ`),
  `x_{\gamma}` (combined key `_{\gamma}` → `ᵧ`).

For a multi-character group with no pre-baked combined key — e.g. `_{test}`
or `^{n2}` — the converter returns the raw input unchanged (output `_{test}`,
output `^{n2}`) instead of `ₜₑₛₜ` / `ⁿ²`. The single-char glyph keys
(`_{t}`→ₜ, `_{e}`→ₑ, `_{s}`→ₛ, `^{n}`→ⁿ, `^{2}`→²) already exist in
`Commands.json` and cover most ASCII letters and digits, but nothing iterates
over a group character-by-character to use them.

This spec adds a per-character fallback so multi-character `_{...}` / `^{...}`
groups convert when individual characters have Unicode sub/superscript glyphs,
on a best-effort basis, with consistent handling for the single-char leaf case.

## Decisions (confirmed)

1. **Missing-glyph fallback policy:** **best-effort mix**. Characters with a
   glyph convert to their sub/superscript form; characters without a glyph are
   kept as the plain (normal-size) character. Example: `_{bad}` → `bₐd`.

2. **Single-char missing-glyph policy:** **strip braces, keep plain char**.
   Example: `x_q` → `xq`, `x^z` → `xz`. This is consistent with the
   best-effort rule for multi-character groups (a one-char group is just the
   degenerate case). It deliberately *changes* the current behavior, which
   leaves the raw `_{q}` / `^{z}`.

## Scope

**In scope**

- New per-character fallback for `_{...}` and `^{...}` groups in
  `LatexConverterService.Convert`.
- Unify the single-char subscript/superscript leaf path with the same
  per-character rule (strip braces — i.e. emit the plain char — when no glyph
  maps).
- Preserve the existing unresolved-command hint signal fed to
  `OverlayViewModel` (`LastUnresolvedCommands`): record the original raw
  `cmd{...}` form whenever the fallback had at least one missing glyph, so the
  overlay still shows a "no Unicode equivalent" hint.
- New xUnit tests covering multi-char, partial-glyph, and single-char
  no-glyph cases.
- Doc update: `docs/architecture.md` parser/precedence section.

**Out of scope**

- Any change to `Commands.json` glyph tables.
- Custom user mappings override behavior (already wins via combined-key
  lookup precedence — unchanged).
- Combining-diacritic modifier commands (`\hat`, `\vec`, `\bar`, …). These keep
  their existing `[cmd]{leaf}` combined-key path; the new fallback only
  applies to the bare `_` and `^` commands.
- TeX rendering / vertical alignment / font sizing of the output characters.
  Output is plain Unicode text, exactly as today.

## Approach chosen: per-character lookup against existing `_commands`

A private helper iterates the (resolved) group content character-by-character
and looks up the existing `$"{cmd}{{{c}}}"` key (e.g. `_{t}` → `ₜ`) in the
same `_commands` dictionary the parser already uses. No new glyph tables are
introduced; the fallback reuses the curated single-char keys already present in
`Commands.json`, so it can never drift out of sync with them.

Rejected alternatives:

- **Dedicated `char → glyph` sub/sup tables** — faster but duplicates the
  already-curated `_{...}` / `^{...}` entries, creating a maintenance burden
  and drift risk. The existing per-char keys are already in `_commands`.
- **Pre-baking combined multi-char keys** (`^{ab}`, `^{n2}`, …) — combinatorial
  explosion for arbitrary-length groups, rejected.

## Design

### `IgnoreAsFallback` change (prerequisite)

`HandleCmds` step 5 today records `$"{cmd}{{{leaf}}}"` (e.g. `_{bad}`, `^{z}`)
into `_unresolvedCommands` for any command not listed in `IgnoreAsFallback`.
`_` and `^` are currently **not** in `IgnoreAsFallback`, so for every sub/sup
group whose combined lookups miss, step 5 already adds an unresolved entry
before the new fallback runs. That causes two problems once the fallback is
in place:

- **Spurious hint for fully-convertible groups**: `_{test}` would get a
  `_{test}` unresolved record from step 5 even though the per-char fallback
  converts it cleanly to `ₜₑₛₜ` — contradicting the behavior matrix (no hint).
- **Double entry for partial groups**: `_{bad}` would be recorded once by
  step 5 and again by the fallback's `HadMiss` branch.

Fix: **add `"_"` and `"^"` to `IgnoreAsFallback`**. Verified safe — there are
no standalone `"_"` or `"^"` keys in `Commands.json` (all sub/sup entries are
of the combined `_{…}` / `^{…}` form), so step 4's
`_commands.TryGetValue(cmd)` never matched for `_`/`^` anyway. The recording
responsibility for sub/sup moves entirely to the new fallback (group branch:
record when `HadMiss`; single-char leaf branch: record on miss), giving
exactly one record per unresolved sub/sup form and zero when conversion
succeeds.

### New helper

A new `private` method on `LatexConverterService`:

```csharp
/// <summary>
/// Best-effort per-character fallback for "_" or "^" over a group whose
/// combined lookups (on resolved and raw group content) both missed.
/// For each char in <paramref name="content"/>, looks up the existing
/// single-char key "{cmd}{{{c}}}" in _commands and appends the glyph when
/// present; otherwise appends the plain char. Returns whether any char had
/// no glyph, so the caller can keep the unresolved hint signal.
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
```

`cmd` is the `char` trigger (`_` or `^`); `$"{cmd}{{{c}}}"` yields `_{t}` /
`^{n}` to match the existing keys. Only called for `_` / `^` — never for
backslash modifier commands.

### Wiring — group branch

Both group branches (the `_` / `^` handler in `ParseMath` and the identical
block in `ParseGroup`) currently do combined-key lookup on the resolved group
content, then, on miss, a retry on raw content (to catch `^{\gamma}`). The new
fall-through is added after both of those miss:

```csharp
var groupContent = ParseGroup(span, ref pos, depth + 1);
var result = HandleCmds([cmd], groupContent);

if (result == $"{cmd}{{{groupContent}}}")
{
    var rawGroupContent = CaptureRawGroup(span, openBrace);
    var rawResult = HandleCmds([cmd], rawGroupContent);
    if (rawResult != $"{cmd}{{{rawGroupContent}}}")
    {
        result = rawResult;
    }
    else
    {
        // NEW: per-char best-effort fallback (e.g. _{test} -> ₜₑₛₜ)
        var (fb, miss) = ConvertSubSupChars(cmd[0], groupContent);
        result = fb;
        if (miss)
            _unresolvedCommands.Add($"{cmd}{{{rawGroupContent}}}");
    }
}

sb.Append(result);
```

`cmd` is the existing `$"_{"` / `$"^"` string already computed in these
blocks; `cmd[0]` passes the underlying `char` to the helper.

Precedence within the group branch (unchanged above the new step, new step
added as the lowest-priority branch):

1. Combined key on resolved content (`^{ab}`, custom multi-char override, etc.).
2. Combined key on raw content (`^{\gamma}` → `ᵞ`).
3. **(NEW)** per-char best-effort fallback.
4. The original unresolved fallback is removed: per-char fallback always
   produces a string (worst case = the plain text of the group), so the raw
   `cmd{...}` is never the final output. The unresolved *signal* is preserved
   separately when `HadMiss`.

### Wiring — single-char leaf branch

The current single-char branches (`else if (pos < span.Length)` in both
`ParseMath` and `ParseGroup`) emit the result of `HandleCmds([cmd], leaf)`,
which today leaves `cmd{leaf}` (e.g. `_{q}`) in the output on a miss. New
behavior strips the braces on miss, matching the best-effort rule:

```csharp
var leaf = span[pos].ToString();
pos++;
var result = HandleCmds([cmd], leaf);
if (result == $"{cmd}{{{leaf}}}")
{
    // no single-char glyph: best-effort -> plain char, no braces
    result = leaf;
    _unresolvedCommands.Add($"{cmd}{{{leaf}}}");
}
sb.Append(result);
```

This is exactly `ConvertSubSupChars(cmd[0], leaf)` specialized to one char;
inlined as above for clarity, or factored to call the helper (designer's
choice during implementation — both produce the same output).

### Code-shape note

The `_` / `^` handling is currently duplicated between `ParseMath` and
`ParseGroup` (the `CaptureRawGroup` + retry pattern appears twice). Both
copies are updated identically. A follow-on refactor to extract one shared
private handler is desirable but **out of scope** for this change to keep the
diff small and review focused; it can be a separate cleanup task.

### Precedence summary (final, highest → lowest)

1. Multi-character combined key on resolved group content (incl. custom
   mapping overrides of the form `^{foo}`).
2. Multi-character combined key on raw group content (e.g. `^{\gamma}`).
3. **(NEW)** per-character single-char glyph keys (`_{t}`, `^{n}`, …) applied
   best-effort across the group.
4. Character with no glyph for this form → plain normal-size character, no
   braces.

`_{...}` / `^{...}` groups are never emitted verbatim after this change; the
worst-case output is the plain (subscript-free / superscript-free) text of the
group.

### Behavior matrix

| Input | Output | Records unresolved hint? |
|---|---|---|
| `x^{2}` | `x²` | no |
| `x_{\gamma}` | `xᵧ` | no |
| `_{test}` | `ₜₑₛₜ` | no |
| `^{n2}` | `ⁿ²` | no |
| `_{bad}` | `bₐd` | yes — records `_{bad}` |
| `x_q` | `xq` | yes — records `_{q}` |
| `x^S` | `xS` | yes — records `^{S}` |
| `^{\gamma}` | `ᵞ` | no |

### Missing-glyph sets (informational, derived from current `Commands.json`)

Subscript (lowercase) **has glyphs**: `a e h i j k l m n o p r s t u v x` — so
missing glyphs: `b c d f g q w y z`.
Subscript (uppercase): most uppercase absent; only `A E H I J K L M N O P R S T
U V X` have glyphs.
Superscript (lowercase): **all** `a`–`z` have glyphs (e.g. `^{z}`→ᶻ,
`^{q}`→𐞥) — no lowercase miss exists.
Superscript (uppercase) **missing glyphs**: `S X Y Z` (the remaining uppercase
letters all have glyphs, some via phonetic-extension forms like `ꟲ`/`ꟳ`);

These sets are **not** hard-coded anywhere in the implementation; the
`_commands.TryGetValue` call is the single source of truth. The sets above
just document the current observable outcome.

## Error handling

- No new allocation beyond a short `StringBuilder` per group (matches the
  existing per-group allocation in `ParseGroup`).
- Depth / safety-counter guards, `MaxNestingDepth`, and the unmatched-brace
  handling in `CaptureRawGroup` are unchanged.
- Fallback only runs for the `_` and `^` branches; all other command and
  escape handling is untouched.
- Empty group content (`_{}`) yields an empty string with no unresolved entry
  (`HadMiss` is false because the loop body never runs).

## Testing

New xUnit cases in `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`:

- `SubscriptGroup` — `_{test}` → `ₜₑₛₜ`.
- `SuperscriptGroup` — `^{n2}` → `ⁿ²`.
- `SubscriptGroupPartialGlyph` — `_{bad}` → `bₐd`, and
  `LastUnresolvedCommands` contains `_{bad}`.
- `SubscriptSingleCharNoGlyph` — `x_q` → `xq`, and `LastUnresolvedCommands`
  contains `_{q}`.
- `SuperscriptSingleCharNoGlyph` — `x^z` → `xz`, and
  `LastUnresolvedCommands` contains `^{z}`.

Existing tests kept and must stay green (regression guard):

- `Superscript` — `x^2` → `x²`.
- `Subscript` — `x_i` → `xᵢ`.
- `SuperscriptCommand` — `x^{\gamma}` → `xᵞ`.
- `SubscriptCommand` — `x_{\gamma}` → `xᵧ`.

No changes required to `OverlayViewModel` or its tests — it already consumes
`LastUnresolvedCommands` generically; the new `_`/`^` entries just appear in
that list when applicable.

## Documentation

Update `docs/architecture.md`, parser/grammar section: add precedence item 3
"per-character single-char glyph fallback for `_{...}` / `^{...}` groups
(best-effort; missing glyphs kept as plain chars; original raw form recorded
in `LastUnresolvedCommands` when any char missed)".

## Risks

- **Behavior change for single-char no-glyph forms:** `x_q` now outputs `xq`
  instead of `x_{q}`. This is intentional (decision 2) and surfaced via the
  unresolved hint. If a user relied on the verbatim `_{q}` output, they lose
  it — but the hint tells them why, and it matches the multi-char rule so the
  converter is self-consistent.
- **Best-effort mixed sizes** can look odd (e.g. `bₐd`). Accepted per
  decision 1; the unresolved hint still fires, so the user is informed.
- **Duplication risk** between the two identical `_`/`^` blocks is mitigated
  by updating both copy-for-copy; the shared-extraction cleanup is tracked as
  out-of-scope follow-on.
