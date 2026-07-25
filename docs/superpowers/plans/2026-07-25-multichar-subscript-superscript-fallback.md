# Multi-Character Subscript/Superscript Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `LatexConverterService.Convert` convert multi-character subscript/superscript groups (e.g. `_{test}`→`ₜₑₛₜ`, `^{n2}`→`ⁿ²`) via a per-character fallback, with a consistent best-effort rule for single-char forms that have no Unicode glyph (e.g. `x_q`→`xq`), while preserving the unresolved-command hint signal.

**Architecture:** Add a private `ConvertSubSupChars(char,string)` helper that iterates group content and looks up the existing single-char keys (`_{t}`, `^{n}`, …) already in `_commands` — no new glyph tables. Wire it into both duplicated `_`/`^` branches (group + single-char leaf) in `ParseMath` and `ParseGroup`. Add `_` and `^` to `IgnoreAsFallback` so `HandleCmds` step 5 stops pre-recording sub/sup forms, leaving the helper as the single recorder (on miss only).

**Tech Stack:** .NET 10, Native AOT, xUnit + NSubstitute (existing test project `tests/LaTeXInserter.Tests`).

**Reference spec:** `docs/superpowers/specs/2026-07-24-multichar-subscript-superscript-fallback-design.md`

## Global Constraints

- Native AOT: no reflection-based JSON, no `[DllImport]`, no runtime IL. Source-gen only.
- No new glyph tables / no edits to `Assets/Commands.json`. Fallback reuses keys already in `_commands` via `$"{cmd}{{{c}}}"`.
- Fallback applies to bare `_` and `^` only — never to backslash modifier commands (`\hat`, `\vec`, …). Their `HandleCmds` combined-key / combining-diacritic path is untouched.
- Precedence (highest → lowest) must hold: (1) multi-char combined key on resolved content (incl. custom override), (2) multi-char combined key on raw content (e.g. `^{\gamma}`→ᵞ), (3) NEW per-char fallback, (4) no glyph → plain char.
- Exact test glyphs are pinned below (verified against `src/LaTeXInserter/Assets/Commands.json` by grep). Do not substitute different characters without re-verifying presence/absence in `Commands.json`.
- Existing parser tests must stay green (regression). New tests append to the existing `LatexConverterServiceTests` class; do not duplicate fixtures.

---

## File Structure

- **Modify:** `src/LaTeXInserter/Services/LatexConverterService.cs` — the `IgnoreAsFallback` set (add `_`,`^`), add `ConvertSubSupChars` helper, update the two `_`/`^` blocks in `ParseMath` (lines ~182–217) and `ParseGroup` (~326–357), update the single-char leaf branch in both.
- **Test (modify):** `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs` — add 5 new `[Fact]` methods; existing facts unchanged.
- **Docs (modify):** `docs/architecture.md` — parser precedence section: add per-char fallback bullet.

Single subsystem. No new files.

---

## Task 1: Add `^` and `_` to `IgnoreAsFallback`

**Files:**
- Modify: `src/LaTeXInserter/Services/LatexConverterService.cs:19-26` (the `IgnoreAsFallback` `FrozenSet`)

**Interfaces:**
- Consumes: none.
- Produces: an `IgnoreAsFallback` set containing `"_"` and `"^"`, so `HandleCmds` step 5 (lines ~430–436) no longer appends `$"{cmd}{{{leaf}}}"` to `_unresolvedCommands` for sub/sup. Recording moves entirely to the new helper (Task 3) and the leaf branch (Task 4).

- [ ] **Step 1: Write a failing test pinning step-5 silence**

Append to `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs` (inside the class, before the closing brace). This test relies on behavior that exists **after** Task 3 too, but it fails against current `master` because today `_{bad}` is recorded by `HandleCmds` step 5 *and* the raw form is returned. It will be re-confirmed green after Task 3.

```csharp
[Fact]
public void SubscriptGroupNoGlyph_NotDoubleReported()
{
    // After all tasks: _{bad} -> bₐd with exactly one unresolved entry.
    // Before IgnoreAsFallback change: HandleCmds step 5 records _{bad} AND
    // (later) the fallback records it again -> count >= 2 or the raw form
    // leaks. This test guards against the step-5 pre-recording.
    var svc = CreateService();
    var result = svc.Convert("_{bad}");
    // behavior lands in Task 3; for Task 1 just assert no entry duplicated:
    // after Task 1 alone, step 5 stops recording, so entry count drops to 0
    // (fallback not yet added). Re-verified at end.
    Assert.Empty(svc.LastUnresolvedCommands);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~SubscriptGroupNoGlyph_NotDoubleReported"`
Expected: FAIL — current code records `_{bad}` in `_unresolvedCommands` via `HandleCmds` step 5, so `Assert.Empty` fails.

- [ ] **Step 3: Edit `IgnoreAsFallback`**

In `src/LaTeXInserter/Services/LatexConverterService.cs`, find the `IgnoreAsFallback` frozen set (lines ~19–26) and append `"^"`, `"_"` to the array literal (kept alphabetical within the added pair for readability):

```csharp
private static readonly FrozenSet<string> IgnoreAsFallback = FrozenSet.ToFrozenSet(
[
    "\\text", "\\mathrm", "\\mathbb", "\\mathbf", "\\mathbfit",
    "\\mathcal", "\\mathfrak", "\\mathsf", "\\mathsfbf", "\\mathsfbfit",
    "\\mathsfit", "\\mathtt", "\\left", "\\right", "\\not",
    "\\overleftrightarrow", "\\overline", "\\underbar", "\\underleftarrow",
    "\\underline", "\\underrightarrow", "^", "_"
]);
```

Why safe: verified there is no standalone `"_"` or `"^"` key in `Commands.json` (all sub/sup entries are the combined `_{…}` / `^{…}` form, see grep in the spec). So `HandleCmds` step 4 (`_commands.TryGetValue(cmd)`) already missed for `_`/`^`; step 5 was the only effect, and we are removing it.

- [ ] **Step 4: Run the new test + existing sub/sup tests to verify**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~Subscript|FullyQualifiedName~Superscript|FullyQualifiedName~SubscriptGroupNoGlyph_NotDoubleReported"`
Expected: PASS for `SubscriptGroupNoGlyph_NotDoubleReported` (now empty). `Superscript`, `Subscript`, `SuperscriptCommand`, `SubscriptCommand` still PASS — for `x^2`/`x_i` the combined key hits, so step 5 never ran; for `x^{\gamma}` the raw retry hits.

- [ ] **Step 5: Commit**

```bash
git add src/LaTeXInserter/Services/LatexConverterService.cs tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs
git commit -m "$(cat <<'EOF'
fix: stop HandleCmds pre-recording _/^ sub/sup forms

Add "_" and "^" to IgnoreAsFallback so step 5 no longer appends
cmd{leaf} to LastUnresolvedCommands for sub/sup. Recording moves to
the per-char fallback (next task). No standalone "_" or "^" keys
exist in Commands.json, so step 4 lookup was already a no-op.

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Add the `ConvertSubSupChars` helper (no wiring yet)

**Files:**
- Modify: `src/LaTeXInserter/Services/LatexConverterService.cs` — add method near `HandleCmds` (~line 442).
- Test: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`.

**Interfaces:**
- Consumes: the `_commands` dictionary (`Dictionary<string,string>`, field on the class).
- Produces: `private (string Result, bool HadMiss) ConvertSubSupChars(char cmd, string content)`.
  - `cmd` is the trigger char `'_'` or `'^'`. `$"{cmd}{{{c}}}"` yields `_{t}` / `^{n}` to match existing keys.
  - `Result` = concatenation, per char, of the sub/sup glyph if `_commands` has `"{cmd}{{{c}}}"`, else the plain char `c`.
  - `HadMiss` = true iff any char had no glyph.
  - Behavior on empty `content`: returns `("", false)` (loop body never runs).

- [ ] **Step 1: Write a failing helper test that exercises it indirectly**

The helper is private, so test via the full `Convert` path… but the wiring lands in Task 3. To test the helper in isolation in Task 2, expose it via an `internal` method and `[InternalsVisibleTo]`. Check whether `InternalsVisibleTo` is already configured:

Run: `grep -r "InternalsVisibleTo" src/LaTeXInserter/` — if nothing, skip isolated-helper testing and instead write the end-to-end tests in Task 3 (Task 2 then has no test of its own; the helper is covered by Task 3 tests). **Decision: keep `ConvertSubSupChars` private and do not add `InternalsVisibleTo`** (avoid touching the csproj/assembly for a tiny helper). Task 2 therefore has no test step; verification is Task 3's tests. Proceed to Step 2.

- [ ] **Step 2: Add the helper method**

Insert this after the `HandleCmds` method (around line 442), before `LoadDefaultCommands`:

```csharp
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
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/LaTeXInserter`
Expected: build succeeds (method is unused → a `CS0169`/`CA1801`-style warning may appear; if the project treats warnings as errors and this breaks the build, temporarily call the method from a `#if DEBUG`-only throw, or just proceed to Task 3 wiring which immediately uses it). If a hard error blocks the build, add a single throw-away call site in `Convert` that is removed in Task 3.

- [ ] **Step 4: Commit**

```bash
git add src/LaTeXInserter/Services/LatexConverterService.cs
git commit -m "$(cat <<'EOF'
feat: add ConvertSubSupChars per-char fallback helper

Private helper iterates group content and looks up existing single-char
sub/sup keys (_{t}, ^{n}, ...) in _commands. No new glyph tables.
Wired in next task.

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Wire the fallback into the group branches

**Files:**
- Modify: `src/LaTeXInserter/Services/LatexConverterService.cs` — the `_`/`^` group handler in `ParseMath` (lines ~182–204) and the identical block in `ParseGroup` (lines ~326–345).
- Test: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs` — 3 new facts.

**Interfaces:**
- Consumes: `ConvertSubSupChars(char,string)` from Task 2; `_unresolvedCommands` (existing `List<string>`); `CaptureRawGroup` (existing static) and `openBrace` (existing local).
- Produces: full multi-char conversion behavior.

**Pinned glyphs/chars (verified by grep against `Commands.json`):**
- Subscript glyphs present: `a`→ₐ, `e`→ₑ, `s`→ₛ, `t`→ₜ. Absent: `b`, `d`, `q`.
- Superscript glyphs present: `n`→ⁿ, `2`→².
- Therefore: `_{test}`→`ₜₑₛₜ`; `^{n2}`→`ⁿ²`; `_{bad}`→`bₐd` (b miss, a glyph, d miss); `_{bad}` records `_{bad}`.

- [ ] **Step 1: Write the 3 failing group tests**

Append to `LatexConverterServiceTests.cs`:

```csharp
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
```

Also update the Task 1 placeholder test `SubscriptGroupNoGlyph_NotDoubleReported` to its final form (now that fallback exists):

```csharp
[Fact]
public void SubscriptGroupNoGlyph_NotDoubleReported()
{
    var svc = CreateService();
    var result = svc.Convert("_{bad}");
    Assert.Equal("bₐd", result);
    // exactly one entry, not two
    Assert.Equal(1, svc.LastUnresolvedCommands.Count(x => x == "_{bad}"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~SubscriptGroup|FullyQualifiedName~SuperscriptGroup|FullyQualifiedName~SubscriptGroupNoGlyph_NotDoubleReported"`
Expected: FAIL — `SubscriptGroup` expects `ₜₑₛₜ` but gets `_{test}`; `SuperscriptGroup` expects `ⁿ²` but gets `^{n2}`; `SubscriptGroupPartialGlyph` expects `bₐd` but gets `_{bad}`.

- [ ] **Step 3: Wire the fallback into the `ParseMath` group branch**

Current `ParseMath` `_`/`^` group branch (lines ~182–204):

```csharp
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
        // This handles cases like ^{\gamma} where ParseGroup resolves \gamma->Unicode
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
```

Replace the inner `if (... == '{')` block's body — specifically the `if (result == ...)` retry block — to add the per-char fallback as a final `else`. Keep `rawGroupContent` capture (already computed above). New body of the `{` branch:

```csharp
if (_hasArg.Contains(cmd) && pos < span.Length && span[pos] == '{')
{
    var openBrace = pos; // save position of '{'
    var rawGroupContent = CaptureRawGroup(span, openBrace);
    var groupContent = ParseGroup(span, ref pos, depth + 1);
    var result = HandleCmds([cmd], groupContent);

    // If unresolved (returned raw "^{...}" or "_{...}"), retry with raw group text.
    // Handles ^{\gamma} where ParseGroup resolves \gamma before HandleCmds sees
    // the combined key "^{\gamma}".
    if (result == $"{cmd}{{{groupContent}}}")
    {
        var rawResult = HandleCmds([cmd], rawGroupContent);
        if (rawResult != $"{cmd}{{{rawGroupContent}}}")
        {
            result = rawResult;
        }
        else
        {
            // Per-char best-effort fallback: _{test} -> ₜₑₛₜ, ^{n2} -> ⁿ².
            // Missing-glyph chars kept as plain; original raw form recorded
            // as unresolved so the overlay hint still fires.
            var (fb, miss) = ConvertSubSupChars(ch, groupContent);
            result = fb;
            if (miss)
                _unresolvedCommands.Add($"{cmd}{{{rawGroupContent}}}");
        }
    }

    sb.Append(result);
}
```

Note `ch` is the char (`'_'`/`'^'`) in scope at the top of the `else if (ch == '_' || ch == '^')` block; pass it directly to `ConvertSubSupChars(ch, …)` — avoids `cmd[0]`.

- [ ] **Step 4: Apply the identical change to the `ParseGroup` `_`/`^` branch**

The `ParseGroup` method has the same `_`/`^` handler (~lines 326–345). Apply the exact same edit: after the raw-retry `if (rawResult != …)` add an `else` with the `ConvertSubSupChars` fallback + `_unresolvedCommands.Add` on miss. In `ParseGroup` the trigger char local is also named `ch` (see `var ch = span[pos];` ~line 279), so the call is identical: `ConvertSubSupChars(ch, groupContent)`.

- [ ] **Step 5: Run the 3 group tests + the double-report test**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~SubscriptGroup|FullyQualifiedName~SuperscriptGroup"`
Expected: PASS — `SubscriptGroup`→ₜₑₛₜ, `SuperscriptGroup`→ⁿ², `SubscriptGroupPartialGlyph`→`bₐd`+1 entry, `SubscriptGroupNoGlyph_NotDoubleReported`→`bₐd`+exactly 1.

- [ ] **Step 6: Run the full converter test class to confirm no regressions**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~LatexConverterServiceTests"`
Expected: ALL PASS — `Superscript` (x^2→x²), `Subscript` (x_i→xᵢ), `SuperscriptCommand` (x^{\gamma}→xᵞ), `SubscriptCommand` (x_{\gamma}→xᵧ), `CommandWithArgument`, `NestedCommandWithArgument`, `EscapedBraces`, `UnknownCommand`, `MalformedInputNoException`, `TextCommandPassesThrough`, custom-mapping tests, unresolved-tracking tests all green.

- [ ] **Step 7: Commit**

```bash
git add src/LaTeXInserter/Services/LatexConverterService.cs tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs
git commit -m "$(cat <<'EOF'
feat: convert multi-char sub/sup groups per-char

_{test} -> ₜₑₛₜ, ^{n2} -> ⁿ² via ConvertSubSupChars fallback after
combined-key + raw-retry miss. Missing glyphs kept as plain chars;
raw form recorded in LastUnresolvedCommands on any miss. Wired into
both ParseMath and ParseGroup _/^ branches.

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Unify the single-char leaf branch (strip braces on miss)

**Files:**
- Modify: `src/LaTeXInserter/Services/LatexConverterService.cs` — single-char `else if (pos < span.Length)` leaf branch in `ParseMath` (~line 205) and `ParseGroup` (~line 346).
- Test: `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs` — 2 new facts.

**Interfaces:**
- Consumes: `HandleCmds` (existing); `_unresolvedCommands`.
- Produces: single-char best-effort behavior consistent with the group rule.

**Pinned chars (verified by grep):**
- Subscript glyph absent: `q` (no `_{q}` key). So `x_q` → `xq`, records `_{q}`.
- Superscript glyph absent: `S` (no `^{S}` key — confirmed: uppercase sup present are A C D E G H I J K L M N O P R T U V W + B,F phonetic; S absent). So `x^S` → `xS`, records `^{S}`.

- [ ] **Step 1: Write the 2 failing leaf tests**

Append:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~SubscriptSingleCharNoGlyph|FullyQualifiedName~SuperscriptSingleCharNoGlyph"`
Expected: FAIL — current leaf branch emits `HandleCmds([cmd], leaf)` which on miss returns `$"{cmd}{{{leaf}}}"` (e.g. `_{q}`); `x_q` → `x_{q}`, not `xq`. (After Task 1, step 5 no longer records it, so `LastUnresolvedCommands` is empty too — `Assert.Contains` fails.)

- [ ] **Step 3: Edit the `ParseMath` single-char leaf branch**

Current (lines ~205–212):

```csharp
else if (pos < span.Length)
{
    // Subscript/superscript of single char
    var leaf = span[pos].ToString();
    pos++;
    var result = HandleCmds([cmd], leaf);
    sb.Append(result);
}
```

New:

```csharp
else if (pos < span.Length)
{
    // Subscript/superscript of single char
    var leaf = span[pos].ToString();
    pos++;
    var result = HandleCmds([cmd], leaf);
    if (result == $"{cmd}{{{leaf}}}")
    {
        // No single-char glyph: best-effort -> plain char (strip braces),
        // matching the multi-char group rule. Record for the overlay hint.
        result = leaf;
        _unresolvedCommands.Add($"{cmd}{{{leaf}}}");
    }
    sb.Append(result);
}
```

- [ ] **Step 4: Apply the identical edit to the `ParseGroup` single-char leaf branch**

`ParseGroup`'s `else if (pos < span.Length)` (~lines 346–352). Identical change (replace `result = HandleCmds([cmd], leaf);` + `sb.Append(result);` with the miss-check + plain fallback + `_unresolvedCommands.Add`).

- [ ] **Step 5: Run the 2 leaf tests**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~SubscriptSingleCharNoGlyph|FullyQualifiedName~SuperscriptSingleCharNoGlyph"`
Expected: PASS — `x_q`→`xq` + `_{q}` recorded; `x^S`→`xS` + `^{S}` recorded.

- [ ] **Step 6: Run the full converter test class**

Run: `dotnet test tests/LaTeXInserter.Tests --filter "FullyQualifiedName~LatexConverterServiceTests"`
Expected: ALL PASS, including `Subscript` (`x_i`→`xᵢ`, i has glyph so `HandleCmds` hits combined key, miss-branch not taken) and `Superscript` (`x^2`→`x²`).

- [ ] **Step 7: Commit**

```bash
git add src/LaTeXInserter/Services/LatexConverterService.cs tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs
git commit -m "$(cat <<'EOF'
feat: strip braces on single-char sub/sup glyph miss

x_q -> xq, x^S -> xS when the char has no sub/sup glyph, consistent
with the multi-char best-effort rule. Records _{q}/^{S} in
LastUnresolvedCommands so the overlay hint still fires.

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Full regression + docs

**Files:**
- Modify: `docs/architecture.md` — parser precedence section (~line 149 onward).

**Interfaces:** none.

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test tests/LaTeXInserter.Tests`
Expected: ALL PASS. Spot-check the count: 5 new converter facts + 1 updated placeholder = 6 new/changed green; no existing fact fails. If any `OverlayViewModelTests` fail, it is NOT caused by this change (those mocks return canned `Convert` strings) — investigate separately and do not modify them.

- [ ] **Step 2: Build the AOT app**

Run: `dotnet build src/LaTeXInserter`
Expected: build succeeds, no new warnings beyond any pre-existing. (If an unused-method warning from Task 2's interim state surfaced, confirm it is gone now that `ConvertSubSupChars` is called.)

- [ ] **Step 3: Update `docs/architecture.md`**

Find the "LaTeX Parser (Recursive Descent)" section (~line 149). After the grammar semantics / existing bullet list describing sub/sup handling, add a bullet:

```markdown
- **Sub/sup group fallback (per-character best-effort):** when `_{...}` / `^{...}` miss both the resolved-content combined key and the raw-content combined key, `ConvertSubSupChars` iterates the group and substitutes each char via the existing single-char keys (`_{t}`→ₜ, `^{n}`→ⁿ, …). Missing-glyph chars are kept as plain (normal-size) chars; the original raw `_{...}` / `^{...}` form is recorded in `LastUnresolvedCommands` so the overlay's "no Unicode equivalent" hint still fires. Single-char forms (`x_q`) follow the same rule: braces stripped to the plain char on miss. `_` and `^` are in `IgnoreAsFallback` so `HandleCmds` does not separately pre-record these forms.
```

- [ ] **Step 4: Commit**

```bash
git add docs/architecture.md
git commit -m "$(cat <<'EOF'
docs: document per-char sub/sup fallback precedence

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

---

## Self-review checklist (run after writing, results inline)

- **Spec coverage:** Spec §"New helper" → Task 2. §"Wiring — group branch" → Task 3. §"Wiring — single-char leaf branch" → Task 4. §"`IgnoreAsFallback` change" → Task 1. §"Testing" 5 cases → Tasks 3–4 (SubscriptGroup, SuperscriptGroup, SubscriptGroupPartialGlyph in T3; SubscriptSingleCharNoGlyph, SuperscriptSingleCharNoGlyph in T4) + 1 double-report guard in T1/T3. §"Documentation" → Task 5. All sections covered.
- **Placeholder scan:** No TBD/TODO/vague. Every code step has the actual code. Test glyphs pinned with grep verification.
- **Type consistency:** `ConvertSubSupChars(char,string) → (string,bool)` in Task 2; called as `ConvertSubSupChars(ch, groupContent)` in Tasks 3–4 (destructures via `var (fb, miss) =`). `IgnoreAsFallback` keys are strings `"_"`,`"^"` (Task 1) consistent with the `FrozenSet<string>` type. `_unresolvedCommands.Add(string)` matches `List<string>` field. No signature drift across tasks.
- **Exact-char audit:** Subscript present `a,e,s,t` (verified `_{a}`→ₐ L2525, `_{e}`→ₑ L2526, `_{s}`→ₛ L2537, `_{t}`→ₜ L2538); absent `b,d,q` (not in `_{[a-z]}` grep). Superscript present `n,2` (`^{n}`→ⁿ L2475, `^{2}`→² L2421); absent `S` (not in `^{[A-Z]}` grep — A,C,D,E,G,H,I,J,K,L,M,N,O,P,R,T,U,V,W present, S absent).

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-25-multichar-subscript-superscript-fallback.md`.
