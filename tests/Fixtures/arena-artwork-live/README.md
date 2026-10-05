# Live Arena artwork regression fixture

This is the user-supplied 2026-10-03 Phase 9B.5 region-only Arena capture (1325 x 1131), copied without changing any pixels. It shows the nine WOE/WOT pack cards and game background, with no account name, chat, desktop or other applications visible.

PNG SHA256: `0524FEC7B15BC8699A81400F2A3677DB3DE33D05E86CB1956344672A4C2ACCA1`.

The nine JPEGs are snapshots of DraftTG's existing small-front visual-reference cache, keyed by exact Scryfall printing ID. Their URIs, Arena IDs, set/collector numbers and artists come from the existing local Scryfall bulk cache and are preserved in manifest.json. No new reference downloads were performed. Each reference is 146 x 204 pixels.

Cards in manifest.json retain the serialized Arena pack order. ExpectedVisualOrder records the user's separate visible-order ground truth for test assertions and report labels only; recognition receives no expected visual order. The five-column layout reproduces the saved calibration profile. No scoring/ranking/statistics data is needed by the matcher.

From the solution directory, after building:

```powershell
dotnet src/DraftTG.App/bin/Debug/net10.0/DraftTG.App.dll --localize-image tests/Fixtures/arena-artwork-live/arena-draft-capture.png --manifest tests/Fixtures/arena-artwork-live/manifest.json --output artifacts/artwork-matcher/after
```

Append `--coarse-only` and use a different output directory to reproduce the original 0/9 coarse-alignment failure with the same references and thresholds.

The command runs without creating an Avalonia window, inspecting Arena, invoking capture, reading current logs or using the network. It writes scores.txt, scores.json and localization-latest.png only to the explicit output directory. See ARTWORK_MATCHER_REPORT.md for the audit and acceptance limits.
