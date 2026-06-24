# Task 2 Report: Parser fix for ^{\cmd} double-lookup

## Status: DONE

## Commits
- `e0bdc7a` fix: resolve superscript/subscript of LaTeX commands via double-lookup

## Test Summary
All 105 tests pass (104 existing + 2 new: SuperscriptCommand, SubscriptCommand)

## Changes

### `tests/LaTeXInserter.Tests/LatexConverterServiceTests.cs`
- Added `SuperscriptCommand` test: `x^{\gamma}` -> `xᵞ`
- Added `SubscriptCommand` test: `x_{\gamma}` -> `xᵧ`

### `src/LaTeXInserter/Services/LatexConverterService.cs`
- Added `CaptureRawGroup` static method: captures raw brace content without resolving commands
- Modified `^`/`_` handler in `ParseMath` (line ~169): captures raw group before `ParseGroup`, then if `HandleCmds` returns unresolved (`^{\gamma}` -> `^{𝛾}`), retries with raw LaTeX form (`{\gamma}`)
- Modified `^`/`_` handler in `ParseGroup` (line ~310): same double-lookup pattern
- Fixed edge case: `CaptureRawGroup` handles unmatched braces (e.g. `x^{` input) gracefully instead of throwing `ArgumentOutOfRangeException`

## Concerns
- The `CaptureRawGroup` method walks the span a second time (once for raw capture, then `ParseGroup` walks it again). This is a minor perf cost but is only triggered for `^{...}` / `_{...}` patterns with brace groups, which is the correct semantic fix.
- The double-lookup pattern is duplicated in both `ParseMath` and `ParseGroup`. A future refactor could extract a shared helper, but the current approach keeps the patch minimal and localized.
