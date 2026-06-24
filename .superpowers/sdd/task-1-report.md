# Task 1 Report: Default PreviewFontSize 14>20 + autocomplete spacing

## Status: DONE

## Changes Made

1. `src/LaTeXInserter/Models/AppSettings.cs:9` -- Changed `int PreviewFontSize = 14` to `int PreviewFontSize = 20`
2. `src/LaTeXInserter/ViewModels/OverlayViewModel.cs:35` -- Changed `_previewFontSize = 14` to `_previewFontSize = 20`
3. `src/LaTeXInserter/Views/OverlayWindow.axaml:73` -- Added `Margin="8,0,0,0"` to the Unicode TextBlock in autocomplete DataTemplate

## Commits

- `aef8e2f` -- fix: default preview font size 14>20, add autocomplete spacing

## Test Summary

All 103 tests passed (0 failed, 0 skipped) in 120ms.

## Concerns

None.
