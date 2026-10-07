# Untapped.gg Draftsmith — live black-box localization audit

Date: 2026-10-07, 07:23–08:15 local (UTC−05:00). Live MTG Arena Quick Draft (`QuickDraft_WOE_20260929`), P1P1 → P1P2.
Subjects: MTGA (Unity 6000.3.14f1, D3D11, Steam install) and Untapped.gg Companion 3.11.0 (Electron), native module `untapped-scry` 6.15.2.
Tooling: [tools/UntappedLocalizationAudit](tools/UntappedLocalizationAudit/README.md) (new, isolated, not in `DraftTG.sln`), plus the existing `tools/ArenaSortOrderProbe`.
Raw evidence: `artifacts/untapped-audit/` (ignored by git; screenshots show the Arena screen name — do not share unreviewed).

**Evidence labels**

| Label | Meaning |
|---|---|
| **CONFIRMED** | Directly observed in this session: handles, window styles, files, Untapped's own log lines, measured pixels. |
| **EXPERIMENTAL** | Measured in a controlled, user-performed experiment. Valid for the conditions tested; sample size is given. |
| **INFERENCE** | Reasoned from confirmed/experimental evidence; not directly observed. |
| **SPECULATION** | Plausible but weakly supported or unsupported. |

No production code was changed. No picks were made except one controlled pick performed by the user (Two-Headed Hunter, P1P1) after explicit request.

---

## 1. Executive summary

Untapped does **not** find cards on screen. It knows *which* cards are in the pack by **reading Arena's process memory**, gets them **already in Arena's display order**, and draws badges into **fixed slot rectangles computed relative to Arena's client area** in a single transparent overlay window.

| # | Finding | Label |
|---|---|---|
| 1 | Pack identity comes from Untapped's read-only **memory reader** ("Scry", `untapped-scry.node`): `[ScryDraftInfo._read] Pack: [...]`. A no-sandbox renderer (PID 47644) holds a persistent `PROCESS_VM_READ` handle to MTGA. Untapped *also* keeps Player.log open, but registered the P1P2 pack only after Scry saw it, ~145 ms **after** the Player.log record. | CONFIRMED |
| 2 | The pack order Untapped uses equals Arena's on-screen order, which equals **DraftTG's Arena-DB sort prediction**, for both live packs (P1P1 non-discriminating; P1P2 discriminating). Player.log order differs in both. | CONFIRMED |
| 3 | All **48 of 48** per-pick orders that Untapped stored locally (`drafts.json`, 3 drafts) fit an Arena-DB sort rule exactly. Together with DraftTG's fixtures and today's two packs they leave **44 / 400 rules (13 functional classes)**. All start with rarity and sort by color next; 12 first split off lands. | CONFIRMED (fit); "they are visual orders" = INFERENCE |
| 4 | Card rectangles are a **fixed 5-column slot grid**. It is scaled uniformly by **client height** and anchored at the **client's horizontal centre**: `s = H/1009; x(c) = W/2 + s·(−634.5 + 183.25·c); y(r) = s·(175.1 + 250.0·r); card = s·(158.5 × 216.8)`. Last row left-aligned; identical for 14 and 13 cards. Fit to 4 datasets at 3 client sizes: RMS 0.40 px. A **pre-registered prediction for 1920×1080** was hit to ≤0.7 px (excluding one legendary-frame edge). Untapped's badges sit at constant offsets ×s from these slots at every size (§20). | CONFIRMED / EXPERIMENTAL |
| 5 | Badges are **independent of Arena's live card objects**: old badges stayed on an empty table for ~1 s after the cards flew away ("ghost badges"), and new badges appeared at final slots *before* the new cards flew in. | EXPERIMENTAL (1 transition) |
| 6 | Overlay = **one** Electron window `Untapped.gg Overlay`, exactly Arena's client rect, `TRANSPARENT\|LAYERED\|NOACTIVATE` (click-through). It is made TOPMOST 68–147 ms after Arena gains foreground and dropped when Arena loses it. No per-card windows. | CONFIRMED / EXPERIMENTAL (n=7) |
| 7 | No screen capture or OCR: none of the capture/OCR APIs are imported by the native modules, no capture library is loaded, and no Untapped process uses measurable idle CPU other than the memory reader. Behaviour (ghost badges, stale layout during rescale) shows no pixel feedback. | CONFIRMED (static) + EXPERIMENTAL (behaviour) |
| 8 | Hover previews are decorated using Arena's **internal rollover state read from memory** (`ScryMtgaDraftRollover`). A "phantom" preview shown while the OS cursor was on the title bar was decorated too. | CONFIRMED (reader) + EXPERIMENTAL (n=1) |
| 9 | Untapped does **not** use `Raw_CardDatabase_*.mtga`: no handle (6 samples) and no reference in any binary or bundle. | CONFIRMED (absence) |
| 10 | Network calls carry **card IDs only** (per-pick `dynamic_card_scores`); localization needs no server round trip. | CONFIRMED |

**Implication for DraftTG:** a simpler, fully deterministic pipeline that needs no memory reading reproduces what Untapped draws: Player.log identity (available ~145 ms *earlier* than Untapped's) → Arena-DB display order → slot geometry from the client rect. Image matching can drop from the per-pick placement path to calibration and verification. See §17.

**Geometry verdict (§20):** the slot formula is strong enough to start a **DraftTG prototype** for 16:9-to-1.7 clients at 13–14 cards, behind a verification gate. Before it is trusted outside those conditions it needs: aspect ratios below ~1.6, packs of ≤5 cards, 15-card packs, and fullscreen/borderless.

---

## 2. Current live configuration (baseline, 07:30:19)

| Item | Value | Label |
|---|---|---|
| Arena process | `MTGA.exe`, PID 67596, started 07:16:25, Steam install | CONFIRMED |
| Arena HWND | `0x4410F2`, class `UnityWndClass`, title `MTGA` | CONFIRMED |
| Window rect | (3922,183) 1739×1048; DWM frame (3929,183) 1725×1041 | CONFIRMED |
| Client rect | (3930,214) **1723×1009** (physical px) | CONFIRMED |
| Mode | Windowed: `CAPTION\|SYSMENU\|MINIMIZEBOX`, no `THICKFRAME`; `SHQueryUserNotificationState` = accepts notifications (no fullscreen app) | CONFIRMED |
| DPI / monitor | 96 DPI on DISPLAY1 2560×1440 @59 Hz at x=3840 (secondary). Primary DISPLAY2 3840×2160 @144 DPI (150 %) | CONFIRMED |
| Arena UIA | Only the Win32 frame (title bar, system menu, min/max/close); **no card descendants** (re-checked) | CONFIRMED |
| Arena card DB | `Raw_CardDatabase_b8e6eb97…mtga`, modified 2026-10-06 15:22; `Data 2026.63.0.4`, `GRP 4.1327304` (replaced since Phase 9E's `2026.63.0.270` file) | CONFIRMED |

During the session the user moved Arena several times (including across monitors); positions below are always given with their client origin.

## 3. Untapped process / window topology

### Processes

| PID | Role | Started | Notes | Label |
|---|---|---|---|---|
| 65772 | Electron main (browser) | 10-06 09:59:35 | parent `explorer`; owns all windows; loads `untapped-node-native.node`, `keytar.node`, `lzma-native`; **holds Player.log open** | CONFIRMED |
| 59400 | GPU process | 10-06 | `d3d11`, `dxgi`, `dcomp` (normal Chromium compositing) | CONFIRMED |
| 71540 | Network utility | 10-06 | `NetworkService` | CONFIRMED |
| 64788 | Renderer, sandboxed | 10-06 | main app window | INFERENCE (start time) |
| 71024 | Renderer, sandboxed | 07:16:27 (2 s after MTGA) | overlay window; no native addons; ~0 % idle CPU | INFERENCE (start time) |
| 47644 | Renderer, **`--no-sandbox --no-zygote`** | 07:16:27 | loads **`untapped-scry.node`**; **holds MTGA handle `0x1410`** | CONFIRMED |

### Windows (Phase B)

| HWND | Title / class | Rect | Visible | Ex-style | Notes |
|---|---|---|---|---|---|
| `0x510C16` | `Untapped.gg Overlay` / `Chrome_WidgetWin_1` | **= Arena client rect** (followed every move) | yes | `TRANSPARENT\|LAYERED\|NOACTIVATE`, `+TOPMOST` only while Arena is foreground | `LWA_ALPHA` 255; display affinity NONE; children: hidden `Chrome_RenderWidgetHostHWND` (accessibility) and a visible `Intermediate D3D Window` from the GPU process (`TRANSPARENT\|LAYERED\|NOREDIRECTIONBITMAP`) |
| `0x1B1226` | `Untapped.gg MTGA Scry Runtime` / `Chrome_WidgetWin_1` | (532,294) 2775×1500 on primary | **no** | `WINDOWEDGE` | hidden worker window for the memory reader |
| `0x56158E` | `Untapped.gg` | 1350×900 | no | | main app window, hidden |
| others | tray/power/system-preferences/IME/zero-size `Chrome_WidgetWin_0` | 0×0 | no | | Electron housekeeping |

**Topology = A (one transparent full-client overlay) + a hidden worker window.** There are no per-card windows, no child windows of Arena, and nothing owned by Arena. All badges and the Draft Rankings panel live in the one overlay. **CONFIRMED.**

Z-order behaviour (**EXPERIMENTAL**, n=7 raises, n=8 drops): the overlay sits at the bottom of the Z order (just above `Progman`) while Arena is not foreground. It becomes TOPMOST and moves above Arena **68–147 ms** after Arena becomes foreground (80, 73, 143, 143, 147, 71, 68 ms). The drop after Arena loses foreground ranged **0–2823 ms** (2823, 558, 30, 5, 0, 0, 141, 136 ms). Each of the 11 raises and drops checked coincides within 3 ms with an Untapped log line `LimitedDataManager.registerDraftScores` (e.g. 07:53:14.889 / 14.890). That line also fires on score updates. The overlay content was fully drawn in the first capture after the raise (+100 ms, 69k changed pixels, no progressive build-up), so the content is rendered while hidden and focus triggers no re-localization.

### UI Automation (Phase C)

| Target | Result | Label |
|---|---|---|
| Arena | Win32 frame only; no card elements | CONFIRMED |
| Untapped overlay | Chromium document. The first levels expose a `Untapped.gg Settings` button and a container at client (214,162) 917×752, which is exactly the card grid's bounding box plus a 13–23 px margin (5·158 + 4·25.25 + 2·13 = 917). A full subtree walk did not finish (>7 min, also with the overlay visible), so badge rectangles were measured from pixels instead (§7). | CONFIRMED |

UIA shows where Untapped drew, not how it found the cards.

## 4. Local files, logs and handles (Phase D/E)

| Location | Contents relevant to localization | Label |
|---|---|---|
| `%LOCALAPPDATA%\Programs\untapped-companion` | Electron app; `resources\app.asar` (72.8 MB); native addons `untapped-scry.node` (901 KB), `untapped-node-native.node` (324 KB), `keytar.node`, `lzma-native` | CONFIRMED |
| `%APPDATA%\untapped-companion\log.log`, `log.old.log` | Untapped's own debug log (ms timestamps). **Contains e-mail, player names and account IDs — redacted everywhere in this report.** | CONFIRMED |
| `%APPDATA%\untapped-companion\drafts.json` | Per draft and pick: `DraftPack` (in display order), `PickedCards`, `PackScores` (`staticScore` / `dynamicScore` per GrpId). **No geometry.** | CONFIRMED |
| `%APPDATA%\untapped-companion\config.json` | Overlay/Draftsmith preferences only (e.g. `pinDraftColorPanel`); no layout constants | CONFIRMED |
| `%APPDATA%\untapped-companion\mtga.proto` | Protobuf schema `wotc.mtgo.gre.external.messaging` (248 messages, 97 enums), header credits HearthSim's `proto-extractor`. Match-message decoding; no draft/rect/position types | CONFIRMED |

**Static string presence** (counts only; no code was extracted):

| Binary | Present | Absent | Label |
|---|---|---|---|
| `untapped-scry.node` | `OpenProcess`, `VirtualQueryEx`, `ReadProcessMemory`, `EnumProcessModules`, `mono_` | `WriteProcessMemory`, `CreateRemoteThread`, `VirtualAllocEx`, `SetWindowsHookEx`, every capture API (`BitBlt`, `GetDIBits`, DXGI duplication, Graphics Capture) | CONFIRMED |
| `untapped-node-native.node` | `GetClientRect`, `ClientToScreen`, `SetWindowPos`, `GetForegroundWindow`, `GetDpiForWindow`, `OpenProcess`, `SetWindowsHookEx` (1) | capture APIs, `WriteProcessMemory`, `SetWinEventHook` | CONFIRMED |
| `app.asar` | `Player.log` (main bundle + an `mtga-log-parser` dependency); `getDisplayMedia` / `capturePage` **only inside Sentry's feedback and crash-screenshot libraries** | `desktopCapturer`, `setDisplayMediaRequestHandler`, `Raw_CardDatabase`, `Raw_ArtCrop`, `MTGA_Data`, `Order_*` | CONFIRMED |

The `app.asar` file list (archive metadata) includes `ScryRuntime`, `ScryProcessConnection`, `ScryIpcHandler`, `scryTypes`, `MtgaOverlay`, and overlays for other Unity games (`SnapOverlay`, `YgomOverlay`, `Sts2Overlay`). **CONFIRMED.**

**Handles** (system handle table; Untapped handles duplicated into the audit process only to identify their targets):

| Question | Answer | Label |
|---|---|---|
| Is Untapped opening **Player.log**? | **Yes.** Main process 65772 holds it open with `FILE_GENERIC_READ` (0x120089), seen in all 6 samples (08:13:51–08:13:58). Its log shows `MtgaLogReader` watch / backtrack / ready | CONFIRMED |
| Is Untapped opening **`Raw_CardDatabase_*.mtga`**? | **No evidence.** No handle in 6 samples and no string reference anywhere. A transient open cannot be excluded without ETW (needs admin; not requested). An earlier 07:44 sample skipped plain read handles and is not counted | CONFIRMED (absence) |
| Arena-directory watch handle | None seen | CONFIRMED |

**Untapped log events** (paraphrased, identifiers removed), CONFIRMED:
- **MTGA start:** `ProcessWatcher` "MTGA started pid=…", followed by `GameOverlay:mtga.create` and `Resolution 1723x1009`. The resolution line appears only at MTGA start, never on move or rescale.
- **Player.log:** `MtgaLogReader` start, watch and backtrack. It also logs `tryDeleteFile …Player.log` at every MTGA start (the effect is unknown; see §18).
- **Scry connection:** `ScryRuntime.connect {"pid":…, "franchise":"MTGA"}`. The main process drives the hidden window with `ScryRuntimeHost.sendToWindow read/ping/start-watcher/stop-watcher`.
- **Scry reads and watchers:** `mtgaMatchInfo`, `mtgaCurrentLanguage`, `mtgaRankInfo`, `mtgaAccountId`, `mtgaPlayBladeWatcher`, `mtgaInventoryWatcher`, `mtgaEventLandingWatcher`, `mtgaDraftWatcher`.
- **Draft-related readers named in stack traces:** `MonoMtgaCurrentScene`, `ScryMtgaDraftRollover` (via `RolloverUpdater`), `ScryDraftInfo`, `mtgaWrapperController`, `MonoMtgaPAPA`, `ScryMtgaInventory`.
- **Reader errors:** `"_instance" is invalid`, `No active scene found`, `Failed to read 1 bytes from remote address …: Only part of a ReadProcessMemory … request was completed`, and `Cache thrashing: 25 consecutive updates … mtgaWrapperController`.
- **Draft flow:** `ScryDraftInfo` "Pack/Pick changed", "Pack: […]", "Pack size changed"; `DraftWatcher` "Found new pack / Found pick"; `LimitedDataManager.registerDraftPack/Pick/Scores`.
- **Not present:** no log lines about capture, OCR, overlay repositioning or card rectangles.

## 5. Player.log relationship (Phase F)

| Pack | Player.log record | Untapped Scry record | Δ | Label |
|---|---|---|---|---|
| P1P1 | `BotDraftDraftStatus` response, `PackNumber 0 PickNumber 0`, logged `7:17:47 AM` (second resolution) | `07:17:48.010` "P-1P-1 ⇒ P1P1", pack at `.014` (preceded by "No active scene found" retries) | 0–1 s | CONFIRMED |
| P1P2 | `==> BotDraftDraftPick` request seen 08:10:27.237; `<== BotDraftDraftPick` response with the 13-card pack seen **08:10:28.38–.40** | `08:10:28.537` "P1P1 ⇒ P1P2", pack at `.540` | **+140–160 ms after Player.log** | EXPERIMENTAL (n=1) |

(Player.log times are when the 20 ms tail poller first saw the line.)

Player.log orders:
- P1P1: `86686, 86733, 86900, 86986, 86785, 86849, 86760, 86702, 86751, 86744, 86878, 86857, 86993, 87040`
- P1P2: `86725, 86786, 86710, 86889, 86978, 86845, 86721, 86702, 86700, 86985, 86897, 86984, 87058`

**Could Untapped build the whole overlay from Player.log + Arena sort + slot layout?** Yes. For both live packs, Player.log IDs sorted by the Arena DB reproduce the exact order Untapped draws (§6), and the slot model reproduces every badge position (§7). That pipeline would have had the P1P2 pack ~145 ms earlier than Untapped did. **INFERENCE (strong).** Untapped nevertheless uses its memory reader for draft packs. **CONFIRMED** for the draft flow; whether Player.log draft records are ignored entirely is INFERENCE.

## 6. Arena local DB relationship (Phase G)

| Pack | Survivors (fixtures only, 120 rules) | Predicted orders | Actual visual (screenshot) | Untapped Scry order | Label |
|---|---|---|---|---|---|
| P1P1, 14 cards | unanimous | 1 | = prediction | = prediction | CONFIRMED |
| P1P2, 13 cards | **discriminating**: 110 rules vs 10 (`rarity>color>creaturesFirst…`) | 2; they differ at positions 5–7 (white commons) | **Kellan's Lightblades → Return Triumphant → Unassuming Sage** = the 110-rule order | = visual | CONFIRMED |

P1P1 visual order: Dawn of Hope · Restless Vinestalk · Icewrought Sentry · Two-Headed Hunter · Graceful Takedown / Besotted Knight · Kellan's Lightblades · Diminisher Witch · Mocking Sprite · Spell Stutter / Feed the Cauldron · Redcap Thief · Titanic Growth · Crystal Grotto.

P1P2 visual order: Syr Ginger · Glass Casket · Hatching Plans · Tanglespan Lookout · Three Bowls of Porridge / Kellan's Lightblades · Return Triumphant · Unassuming Sage · Aquatic Alchemist · Fell Horseman / Ratcatcher Trainee · Return from the Wilds · Gingerbrute.

**P1P2 refutes the 10 `creaturesFirst`-after-color rules.** The creature Unassuming Sage is third among the white commons, not first. **CONFIRMED.**

**Untapped's stored orders as extra evidence** (offline analysis, `score_untapped_orders.py`; DraftTG's ledger was not touched):

| Evidence set | Surviving rules | Functional classes (655-card universe: ANA, FRA, USG, WOE, WOT) |
|---|---|---|
| DraftTG fixtures | 120 / 400 | 48 |
| + today's two screenshot-confirmed packs | **110** | 43 |
| + 48 Untapped-stored pack orders (3 drafts) | **44** | **13** |

- 48/48 Untapped orders are reproduced exactly by at least one rule. A non-sorted order of 5+ cards would essentially never fit by chance. **CONFIRMED (fit).**
- All 44 survivors start with rarity and include color. 32 use color as the second key; 12 first split off lands (`landLast` 6, `basicLandsFirst` 6) and then sort by color. The 13 classes differ only in land placement and the within-color tie-break (grpId / title / collector number).
- Treating Untapped's stored orders as *visual* orders relies on "Scry order = visual order", which was checked on 2 packs. **INFERENCE.**
- Untapped itself does not touch Arena's DB (§4); its order arrives already sorted from memory. Whether it is Arena-native or sorted inside the Scry reader is unresolved, and it is irrelevant for DraftTG.

## 7. Overlay rectangle measurements (Phase H/I)

### Arena slot grid

Measured on overlay-free captures, verified by drawing the model over the capture. **CONFIRMED.**

> **Superseded by §20.** The first pass used a luminance threshold and reported top 178 / card 158×213. The holder-edge method of §20 is more precise: every card sits in a uniform RGB (38,38,38) holder, and its outer edge gives top **175** and card **158.5×216.8** at 1723×1009. The luminance pass had cut off the cards' dark bottom strip. Columns, pitch, bottom edge and conclusions are unchanged.

All values are client px at client size 1723×1009 (96 DPI), from the §20 joint fit:

```
slot(i) = ( x = 227.0 + 183.25·(i mod 5),  y = 175.1 + 250.0·⌊i/5⌋,  w = 158.5,  h = 216.8 )
columns = 5, rows filled left→right, top→bottom, last row LEFT-aligned (not centered)
14 cards → 5/5/4 ; 13 cards → 5/5/3 ; identical origin, pitch and size
gap between cards: 24.8 px horizontal, 33.2 px vertical
```

How this scales with the client size (height-scaled, centred horizontally) is measured and validated in **§20**.

At 2587×1516 (144-DPI monitor) the layout scaled uniformly by 1.5. **EXPERIMENTAL (spot-checked on half-scale frames, ±3 px)**; this matches §20's formula, since s = H/1009 = 1.5 there.

### Untapped badge rectangles vs slots

Badges were isolated as connected components of an Arena-only frame vs. the same frame with the overlay, captured 50 ms apart.

| Element | P1P1 (full-res diff, 14 cards) | P1P2 (half-res diff, 13 cards) | Formula | Label |
|---|---|---|---|---|
| Header strip (ratings, "U" score, collection diamonds) | x = slotX + 6–7, y = slotY + 20 (with §20's top), ≈146×24 | consistent within measurement error | `(slotX + 6.3·s, slotY + 20.2·s, 145.5·s, 24·s)` | CONFIRMED |
| Shield (Untapped score) | centre x = slot centre x ±0.5; bottom = (slotY + h) + 21…24 (43–62 px tall depending on flame art) | centre x ±4, consistent | centred on the slot's bottom edge; bottom = slotY + h + 21.7·s (median) | CONFIRMED |
| Draft Rankings panel | client (9…177, 95…525), fixed | same | client-anchored, top-left | CONFIRMED |

The offsets are constant across all slots (s = H/1009, §20), so every badge is a deterministic transform of a slot rectangle. **CONFIRMED.**

**Correction during the audit.** Dawn of Hope (P1P1) and Hatching Plans (P1P2) are WOT bonus-sheet cards drawn with a glowing frame. My first reading of Dawn of Hope as "enlarged by hover" was wrong. Its badge sitting on the slot is therefore weak evidence; §10 replaces it.

## 8. Window-move experiment (Phase J)

The user dragged Arena three times, at 08:00:30, 08:00:47 and 08:01:14. Geometry and Z order were sampled every ~4 ms. **EXPERIMENTAL.**

| Metric | Value |
|---|---|
| Mechanism | The whole overlay window is moved and resized to the client rect; badge-to-card offsets cannot change (client-relative content). **CONFIRMED** |
| Freshness when it updates | It lands where Arena's client was a median **4.8 ms** earlier (p10 0, p90 20.5, max 30.7 ms; n=84) |
| Misaligned episodes | median **18 ms**, p90 48 ms, max **210 ms** (n=68). Includes ~150 ms freezes while Arena kept moving, e.g. 300 px of Arena travel with no overlay update |
| Player.log during moves | unchanged (no draft lines) |
| CPU | no spike attributable to capture; the memory reader (47644) averages 7.4 % of a core even when idle, everything else ≈0 % (20 s, §13) |

The irregular freezes fit periodic `GetClientRect`-style polling on a busy thread better than an OS location-change hook; `SetWinEventHook` is absent from both native modules. **INFERENCE.** Either way, the single client-sized overlay window supports client-relative coordinates, not card-by-card recognition.

## 9. Resize experiment (Phase K)

There was no manual resize. Dragging Arena across monitors with different DPI made Windows rescale it: client 1723×1009 → **2587×1516** (×1.5) and back. **EXPERIMENTAL (n=1 each direction).**

- **Overlay window size:** followed within ~20–30 ms. It came out at 2588×1517/1518, within 1–2 px of the client, consistent with DIP rounding at 150 %.
- **Arena:** redrew the draft at the new size by ~t+95 ms.
- **Untapped's badges:** still drawn at their **old-scale positions** at t+95 ms (strips at the 1723-px layout's coordinates inside the larger window), re-laid out by **t+170 ms**. That is a transient misplacement of ~80–160 ms.
- **Badges during the rescale:** no temporary disappearance and no gradual search. They snapped from the stale layout to the new layout.
- **Sort order:** unchanged.

Both monitors have the same aspect ratio, so this cannot tell a layout formula from layout read out of memory (§18).

**Controlled resizes (geometry experiment, §20).** The user changed Arena's windowed resolution in Arena's own settings: 1723×1009 → 1366×768 → 1280×720 → 1920×1080, all at 96 DPI.
- The overlay window matched every new client rect. CONFIRMED.
- Untapped wrote no new `Resolution` log line; that line appears only at MTGA start. CONFIRMED.
- Badges were correctly placed at each size once settled (§20). Transitions were not frame-sampled this time.

## 10. Hover experiment (Phase L)

The user hovered Graceful Takedown and Spell Stutter without clicking. Captures ran at ~40 ms, half scale.

| Observation | Label |
|---|---|
| Arena's draft hover draws a cyan outline on the grid card. The **grid card does not move or scale**. About 250 ms later Arena shows a large **preview** to the right of the hovered card, with keyword tooltips and flavour text. | EXPERIMENTAL |
| Untapped's grid badges do not change during hover. | EXPERIMENTAL |
| Untapped **decorates the preview**: a strip ("3.5 U 44", "4/4 collected"), a shield at the preview's bottom edge, and a rating panel below. It fades in starting ~50 ms after Arena's preview appears (72145 → 72197 ms) and is drawn at the preview's **final** geometry while Arena is still scaling the preview up. It does not track the animation. | EXPERIMENTAL |
| Untapped's log names the source: `MtgaDraftWatcher.update → RolloverUpdater.update → ScryMtgaDraftRollover.read → readMonoReader`. "Rollover" is Arena's hover preview. | CONFIRMED (reader exists); what it returns = INFERENCE |
| **Phantom hover:** after the DPI rescale Arena showed a Two-Headed Hunter preview while the OS cursor was logged on the **title bar** (client (1846,−18), t=48545–48621 ms). Untapped decorated it anyway. Untapped follows Arena's **internal** rollover state, not cursor hit-testing. | EXPERIMENTAL (n=1) |

## 11. Pack-change experiment (Phase M)

One controlled pick: Two-Headed Hunter, P1P1 → P1P2 (13 cards). Frames every ~40 ms; both logs tailed. **EXPERIMENTAL (n=1).**

| Wall clock | Event | Source |
|---|---|---|
| 08:10:27.237 | `==> BotDraftDraftPick` (pick confirmed) | Player.log |
| 27.28–27.73 | Arena flies the 14 cards away | frames |
| ≈27.7–28.55 | **All 14 old badges remain over an empty table** (~0.8 s fully empty; ~1.1 s from the first departures), including the picked card's "42", through "Waiting on other players to pass a draft pack…" | frames |
| 28.326 | Scry: `Pack size changed: 14 => 0` (badges *not* cleared) | Untapped log |
| ≈28.38–28.40 | `<== BotDraftDraftPick` with the P1P2 pack | Player.log |
| 28.537–28.545 | Scry: P1P1⇒P1P2, `Pack: [86984, 86700, 87058, …]` (visual order); "Found pick for P1P1: 86857"; registerDraftPack | Untapped log |
| ≈28.55 | **All old badges vanish in one frame** | frames |
| 28.551 | `POST /api/v1/limited/dynamic_card_scores/…` with `offered_grpids`, `picked_mb_grpids` | Untapped log |
| ≈28.72–28.80 | **New strips and empty shields appear at all 13 final slots**, before any new card has arrived; first cards in flight are not under their strips | frames |
| ≈28.76–29.50 | Arena flies the 13 new cards in; they land under the already-placed badges | frames |
| 29.726 | Score response (1175 ms) | Untapped log |
| ≈29.75 | Shield numbers filled in | frames |

Conclusions:
- **Badge geometry is decoupled from Arena's card objects.** Badges persisted for ~1 s with no cards under them and pre-empted the cards' arrival. A reader of live card-holder transforms could not produce either effect. **EXPERIMENTAL.**
- **New-pack badges are driven by Untapped's logical pack state** (memory reader), ~145 ms after Player.log and ~0.7 s before Arena's cards settle. **EXPERIMENTAL.**
- The 13-card layout reuses the 14-card slots (§7). **CONFIRMED.**

## 12. Process-memory evidence (Phase N)

| Evidence | Result | Label |
|---|---|---|
| Handles to `MTGA.exe` | Renderer **47644** holds handle `0x4EC` → MTGA with access **`0x1410` = `PROCESS_VM_READ \| QUERY_INFORMATION \| QUERY_LIMITED_INFORMATION`**, persistent (07:44 and 08:13, 7 samples). No other Untapped process holds an MTGA handle. No `VM_WRITE`, `VM_OPERATION` or `CREATE_THREAD` | CONFIRMED |
| Module owning that process | `untapped-scry.node` loaded in 47644 only | CONFIRMED |
| Imports | `OpenProcess`, `VirtualQueryEx`, `ReadProcessMemory`, `EnumProcessModules`; no write or injection APIs | CONFIRMED |
| Untapped's own log | Partial `ReadProcessMemory` failures at remote addresses; Mono object readers | CONFIRMED |
| Injection into Arena | No Untapped module in MTGA's module list. Foreign modules there are Steam overlay/client and Defender AMSI only | CONFIRMED |
| Official statement | Read-only memory access with standard OS APIs; no injection or hooking (§14) | CONFIRMED (vendor claim) |

**Answer: yes. Untapped reads Arena's process memory, read-only, from a dedicated no-sandbox renderer.** No memory was dumped and no object layouts were reconstructed in this audit.

## 13. Network metadata (Phase O)

All metadata comes from Untapped's own log; there was no traffic interception. Identifiers are redacted.

| When | Endpoint (host `api.mtga.untapped.gg` unless noted) | Payload |
|---|---|---|
| Event landing | `GET /api/v1/limited-info/<event>/free` and `/premium`, `GET /api/v1/limited-live-events` | none |
| Every new pack | `POST /api/v1/limited/dynamic_card_scores/<event>` | `offered_grpids`, `picked_mb_grpids`, `picked_sb_grpids`; latency 359 ms (P1P1), 1175 ms (P1P2) |
| Background | collection and wildcard uploads, spoilers, tags, analytics, OAuth refresh; telemetry to Mixpanel, Sentry, `metrics.hearthsim.net` | not localization |

- **No request carries screen, window or geometry data.** Strips with static scores were drawn ~0.9 s before the dynamic-score response; shields filled after it. **Localization is local; only ratings are networked.** CONFIRMED.
- `Get-NetTCPConnection` showed no persistent Untapped connections at idle (request-scoped connections). CONFIRMED.
- Idle CPU over 20 s: memory reader 7.4 % of one core (p90 15 %), main, GPU, network and overlay ≈0 %. CONFIRMED.

## 14. Public documentation findings (Phase P)

| Source | Statement (paraphrased; short quotes) | Type |
|---|---|---|
| [HearthSim help: How does the Untapped.gg Companion work (MTGA)](https://help.hearthsim.net/en/articles/5020719-how-does-the-untapped-gg-companion-work) | The Companion watches Player.log in real time and reacts to new lines | Official |
| [HearthSim help: Is the Companion safe to use?](https://help.hearthsim.net/en/articles/7121040-is-the-untapped-gg-companion-safe-to-use) | Uses standard Windows/macOS APIs to access game memory in "read-only mode"; "does not inject or 'hook' any code"; no game-file modification | Official (all supported games) |
| [HearthSim help article 8377705](https://help.hearthsim.net/en/articles/8377705-how-does-the-untapped-gg-companion-work) | Same read-only memory statement; filed under Yu-Gi-Oh! Master Duel, so not used as MTGA evidence on its own | Official (other game) |
| [mtga.untapped.gg/companion](https://mtga.untapped.gg/es/companion) | Ratings superimposed on the draft screen; adaptive Draftsmith ratings; no technical details | Marketing |
| `mtga.proto` header (local) | Generated by HearthSim's public `proto-extractor` | Local, official tooling |
| [AetherHub](https://aetherhub.com/Article/Why-you-should-use-MTG-Arena-DeckTrackers) and similar | Claims Wizards accepts trackers that use "known game information" | Community; **no official Wizards statement found — SPECULATION** |

No public source describes how Draftsmith locates cards or positions the overlay; this audit's behavioural evidence is the only source for that.

## 15. Hypothesis evaluation (Phase R)

| Hypothesis | Supporting | Contradicting | Verdict / confidence |
|---|---|---|---|
| **H1** Player.log IDs + deterministic sort + deterministic slots | Geometry is a deterministic slot grid (§7, §11); Untapped reads Player.log; the order equals a deterministic DB sort | Draft IDs come from the memory reader and arrive after Player.log; no DB access, the order arrives pre-sorted | **Geometry part true, identity part false** for Untapped. High |
| **H2** Player.log IDs + screenshot localization | — | No capture/OCR APIs or libraries; ~0 % idle CPU outside the reader; ghost badges on an empty table; pre-placed badges; stale layout for 80–160 ms after rescale | **Rejected.** Very high |
| **H3** Player.log IDs + memory for order/layout | Memory provides order (pack arrives in display order) and hover state | IDs also come from memory, not Player.log; grid layout is not taken from live card objects | **Partially true** (memory for identity, order and UI state; not grid geometry). High |
| **H4** structural in-memory UI / card-holder representation | Reads structural UI state (scene, rollover, draft info) | Grid badges ignore card objects (ghost badges, pre-placement, no animation tracking) | **True for UI state, false for card rectangles.** High |
| **H5** Arena local DB ordering + deterministic layout | Order equals the DB sort (2/2 live, 48/48 stored); deterministic layout confirmed | No DB handle or reference: Untapped does not do this itself | **Rejected as Untapped's mechanism; confirmed as an equivalent model.** High |
| **H6** hybrid with visual verification | — | No capture path; no correction when cards weren't there | **Rejected** (no visual verification). High |

**Best-supported architecture:**
- **Identity and order:** Scry memory reader (Mono object graph, polled). CONFIRMED.
- **Hover and scene state:** also from memory. CONFIRMED.
- **Match events:** Player.log. CONFIRMED (official + handle).
- **Geometry:** a fixed slot model keyed by pack index, placed inside one client-sized transparent overlay that tracks Arena's HWND client rect. CONFIRMED (behaviour). The slot constants themselves are computed by Untapped from the client size rather than read from memory (INFERENCE, moderate; §18).
- **Ratings:** server API.

**Q6 (structural vs inferred rectangles): inferred from a deterministic layout.** Untapped never shows knowledge of where Arena actually drew a card. EXPERIMENTAL / CONFIRMED.

## 16. DraftTG comparison (Phase Q)

| Capability | DraftTG | Untapped observed | Evidence |
|---|---|---|---|
| Pack identity | Player.log | Arena memory (`ScryDraftInfo`); Player.log tailed for match events | §4, §5, §12 |
| Card order | Arena DB model candidate | Arrives in display order from memory; identical to the Arena-DB prediction | §6 |
| Card rectangles | WGC artwork matcher | Fixed 5-column slot grid relative to the client rect; no per-card localization | §7, §11 |
| Window tracking | HWND | HWND client rect (`GetClientRect`/`ClientToScreen` in a native module); median 4.8 ms fresh, stalls ≤210 ms | §8 |
| Overlay rendering | Avalonia transparent windows | One Electron window = client rect, click-through, TOPMOST only while Arena is foreground | §3 |
| Memory reading | No | Yes, read-only, `PROCESS_VM_READ` | §12 |
| Screen capture | WGC | None | §4, §13, §15 |
| OCR | No | None | §4 |
| Hover preview | — | Decorated, from Arena's rollover state in memory | §10 |
| Pack change | Waits for a settled frame to match artwork | Old badges linger (~1 s ghosting); new badges at final slots ~0.7 s before cards settle | §11 |
| Ratings | 17Lands data (local) | Per-pick server call (static then dynamic scores) | §13 |
| Arena DB | Reads `Raw_CardDatabase` read-only | Not used | §4 |

## 17. Recommended architecture changes

No code was changed. These are proposals, ordered by value.

1. **Make slot geometry the primary placement path (Phase 9E.2 candidate).**
   - Add a pure `ArenaDraftSlotLayout(clientSize, index, count)` implementing §20's formula. Badges then become `transform(slot)`, as Untapped's are.
   - Benefits: placement at pack arrival instead of after the ~0.7–1.2 s fly-in; immune to hover outlines, bonus-sheet glows and animation; no per-pick image work.
   - Keep WGC + the artwork matcher as **verification** (spot-check one or two slots per pack; fail closed to the current visual path on mismatch) and as the tool that measures any layout regime not yet validated.
   - Validated: 13–14 cards at client aspect 1.708–1.778, sizes 1280×720 to 1920×1080 (and a ×1.5 DPI rescale). Before relying on it elsewhere, validate the remaining regimes (§19).
2. **Feed the order model from Player.log + Arena DB, as already designed in Phase 9E.**
   - Untapped's own data shows the order is a pure DB sort (rarity, then color, then a tie-break still to be pinned down).
   - Use unanimous predictions for placement now and fall back to visual matching when predictions are discriminating. Today that covers P1P1-type packs; P1P2 was discriminating before today's evidence.
   - Keep Gate 1's fail-closed contradiction handling across Arena `Data` versions. The DB file changed on 2026-10-06; the model still held on the new version.
3. **Record today's two live packs through DraftTG's normal pipeline** (or by hand with the probe's `observe`) so they count in DraftTG's own ledger. P1P2 alone removes the 10 `creaturesFirst` rules.
   - Untapped's stored orders are useful *offline research* evidence. They should not become a production input, since that would be a dependency on another app's private file.
4. **Overlay window strategy (match or beat Untapped).**
   - One client-sized, click-through overlay.
   - TOPMOST only while Arena is foreground (Untapped needs 68–147 ms to raise; DraftTG can react to `EVENT_SYSTEM_FOREGROUND` immediately).
   - Track moves with `EVENT_OBJECT_LOCATIONCHANGE` on Arena's HWND rather than polling, to avoid Untapped's 50–210 ms stalls.
   - Re-lay out synchronously on client-size or DPI change; Untapped shows a stale layout for 80–160 ms.
5. **Avoid ghost badges.**
   - Hide badges when Player.log shows the outgoing pick request (`==> BotDraftDraftPick` / `EventPlayerDraftMakePick`). Untapped kept stale badges for ~1 s.
   - Show the next pack's badges when the response arrives. Player.log is ~145 ms ahead of Untapped's memory reader. Optionally fade in to match the fly-in.
6. **No memory reading needed for parity on the pack grid.** Memory reading adds only hover-preview decoration and independence from log format changes. It costs per-build Mono layout maintenance (Untapped's log shows recurring reader errors and cache thrashing) and policy risk; no official Wizards statement was found (§14). Not recommended for DraftTG's goals.
7. **Player.log robustness.** Untapped keeps Player.log open read-only and logs a `tryDeleteFile` on it at every MTGA start. DraftTG's tailer should tolerate truncation or recreation at Arena start, and must open with full sharing (as it presumably does).

## 18. Confidence and unresolved questions

| Question | Status |
|---|---|
| Memory reading, single overlay window, pack identity source, no capture/OCR, no DB use | Resolved, high confidence |
| Badges are a fixed transform of a slot grid, independent of card objects | Resolved, high (one pick transition; consistent across all frames) |
| Arena's slot geometry across client sizes | **Resolved for aspect 1.708–1.778 and 720–1080 px height**: height-scaled, horizontally centred (§20). RMS 0.40 px; pre-registered out-of-sample prediction ≤0.7 px |
| Does Untapped place badges with the same geometry? | **Yes, numerically:** constant ×s offsets from the formula's slots at 3 sizes, within ~1 px (CONFIRMED). **Shared implementation: not established** (INFERENCE either way) |
| Are Untapped's slot constants hard-coded/computed, or read once from Arena's layout in memory? | **Unresolved.** Both give identical placements at every size tested. Ghost badges and the stale layout during rescale favour computed constants (INFERENCE, moderate) |
| Hover-preview geometry: formula or memory? | Unresolved. Hover *state* comes from memory (confirmed reader) |
| Layout at aspect < 1.7 (16:10, 4:3, 5:4), fullscreen/borderless, ≤5-card packs, 15-card packs | **Untested.** Arena's windowed list offers only 16:9 sizes (960×540…2560×1440) on this monitor, so narrower aspects cannot be selected there. Below aspect ≈1.26 the formula would push the grid off the left edge, so a breakpoint must exist (INFERENCE) |
| Where did the 1723×1009 (non-16:9) client come from? | Unknown. It is not in Arena's resolution list; non-16:9 clients clearly occur in practice |
| Premier/Traditional (human) drafts | Untested live (two stored drafts fit the DB rules) |
| Is Scry's pack order Arena-native or sorted inside the reader? | Unresolved; irrelevant to DraftTG |
| Purpose of `SetWindowsHookEx` in `untapped-node-native` | Unknown. No DLL appears inside MTGA, so not hook-based injection (CONFIRMED); low-level input hooks are SPECULATION |
| Effect of `tryDeleteFile …Player.log` at MTGA start | Unknown; Player.log content was intact this session |
| Topmost drop delay varied 0–2.8 s | Cause unknown |
| DB order tie-break | 13 functional classes remain: rarity, color, land placement, then grpId / title / collector |

## 19. Exact next experiments

1. ~~Aspect-ratio change~~ — done for 1.708 and 1.778 (§20). Narrower aspects are not offered by Arena's windowed list on this monitor. They remain the main untested regime, reachable on a 16:10 or 4:3 monitor, or wherever non-16:9 clients like 1723×1009 arise.
   - Recipe: `geom` recorder → `measure_grid.py` → `fit_geometry.py --predict <WxH>` (pre-register) → compare.
   - Also run `badges_vs_slots.py`: if Untapped drifts while Arena follows a different rule, that would show Untapped uses a fixed formula rather than Arena's layout.
2. **Late picks with ≤5 cards.** Does Arena keep 5 columns and left alignment, or centre/enlarge? Observe P1P10–P1P14 during normal drafting; no extra picks needed. Run `measure_grid.py` on an overlay-free capture and the `fit_geometry.py` residual check.
3. **Fullscreen / borderless.** Repeat `topology` + `trigger`: does the overlay still match the client rect, and does exclusive fullscreen hide it?
4. **15-card packs** (any Play Booster set): verify 5/5/5.
5. **Premier draft:** compare `Draft.Notify` pack order vs visual vs DB prediction.
6. **DraftTG-native confirmation:** run DraftTG through a full draft so the Phase 9E ledger records its own observations (Gate 1). Today's evidence suggests the tie-break classes will collapse quickly.
7. *(Optional, needs approval: admin + new tool)* An ETW file-I/O trace during Arena start, to settle whether Untapped ever opens `Raw_CardDatabase` transiently and what `tryDeleteFile` does.

## 20. Slot-geometry experiment across client sizes

Run 2026-10-07 08:32–08:46. Draft left at P1P2 (13 cards); no picks. The user changed Arena's windowed resolution in Arena's own Graphics settings, always at 96 DPI on the 2560×1440 monitor. Tooling: the `geom` recorder plus `measure_grid.py`, `badges_vs_slots.py` and `fit_geometry.py` in [tools/UntappedLocalizationAudit](tools/UntappedLocalizationAudit/README.md).

### 20.1 Method

| Step | Detail |
|---|---|
| Arena-only frames | Each time the user brought Arena to the front, full-resolution grabs were taken at **+1 ms and +30 ms**, before Untapped raises its overlay (it needs ≥68 ms). The overlay's Z order was logged before and after each grab. Both frames at 1920×1080 were pixel-identical. |
| Overlay frames | **+0.7 s and +2.5 s**, with the overlay topmost. |
| Card edges | Arena draws each card inside a uniform **RGB (38,38,38) holder** on a reddish table. A card edge is the first non-holder pixel after a holder run, taken as the median of 36–106 scanlines per edge. |
| Excluded cards | Bonus-sheet (WOT) cards draw a glow over their holder and are excluded automatically (Hatching Plans here). The legendary frame of Syr Ginger (r0c0) shifts its detected top edge by 1–2 px; it is kept and visible in the residuals. |
| Badges | Connected components of \|overlay frame − Arena-only frame\|, classified as header strip or shield relative to each card. |
| Fitting | Each model's six reference parameters (x₀, pitch x, y₀, pitch y, w, h at 1723×1009) were fitted by least squares jointly over all datasets. Residual = measured − predicted, client px. |
| Out-of-sample test | Model E was refitted on 1723×1009 + 1280×720 only, and its predictions for 8 candidate sizes were written to `validation/predictions-preregistered.txt` at **08:44:04**, before the user switched to 1920×1080. |
| Resolution list | **Arena's windowed list on this monitor contains only 16:9 sizes:** 960×540, 1024×576, 1280×720, 1366×768, 1600×900, 1920×1080, 2048×1152, 2560×1440 (user screenshot). 4:3 and 16:10 could not be selected. CONFIRMED |

### 20.2 Measured geometries

All values are client px; slot = card outer rectangle.

| Client (aspect) | Pack | Overlay window | Column x (c0…c4) | Row y (r0…r2) | Card w×h | Pitch x / y | Anchors | Layout |
|---|---|---|---|---|---|---|---|---|
| **1723×1009** (1.708) | P1P2, 13 | = client (3828,244) 1723×1009 | 227, 410, 594, 777, 960 | 175, 425, 675 | 158–159 × 215–217 | 183.25 / 250.0 | x₀−W/2 = −634.5 (−0.6289·H); y₀ = 0.1734·H | 5 cols × 3 rows (5/5/3), last row left-aligned |
| 1723×1009 (1.708) | P1P1, 14 | = client | 227, 410, 594, 777, 960 | 175, 425, 675 | 158–159 × 217 | 183.25 / 250.0 | same | 5/5/4, left-aligned |
| **1280×720** (1.778) | P1P2, 13 | = client (4124,377) 1280×720 | 187, 318, 449, 580, 710 | 125, 303, 482 | 112–113 × 154–155 | 130.75 / 178.5 | x₀−W/2 = −453 (−0.6292·H); y₀ = 0.1736·H | 5/5/3, left-aligned |
| **1920×1080** (1.778) | P1P2, 13 | = client (3772,215) 1920×1080 | 281, 477, 673, 869, 1065 | 187, 455, 723 | 170 × 232 | 196.0 / 268.0 | x₀−W/2 = −679 (−0.6287·H); y₀ = 0.1731·H | 5/5/3, left-aligned |

Card size and both pitches scale exactly with **H** (ratios vs 1723×1009: 0.7135–0.7146 = 720/1009, and 1.0704 = 1080/1009). They do not scale with W (1280/1723 = 0.743). The grid's offset from the client's horizontal centre is a constant **−0.629·H** at all three sizes, while its left edge as a fraction of W changes (0.132 → 0.146). **CONFIRMED.** The overlay window equalled the client rectangle at every size. **CONFIRMED.**

### 20.3 Candidate models and residuals

Each model was fitted jointly on all four datasets (49 cards × 4 coordinates). Values are max \|residual\| / RMS, in px.

| Model | 1280×720 | 1723×1009 | 1920×1080 | Verdict |
|---|---|---|---|---|
| **A** fixed pixel grid, DPI only | 215.5 / 90.5 | 34.5 / 16.8 | 139.5 / 57.1 | rejected |
| **B1** uniform s = H/1009, left anchor | 15.0 / 7.04 | 16.7 / 7.80 | 22.0 / 10.5 | rejected |
| **B2** uniform s = W/1723, left anchor | 10.8 / 5.20 | 12.5 / 6.03 | 16.1 / 7.68 | rejected |
| **B3** uniform s = min(W/1723, H/1009), left | 15.0 / 7.04 | 16.7 / 7.80 | 22.0 / 10.5 | rejected (≡ B1 here) |
| **C/D** independent sx = W/1723, sy = H/1009 (≡ fixed client-% grid) | 9.1 / 3.33 | 12.5 / 4.01 | 14.2 / 4.88 | rejected |
| **E** uniform s = H/1009, **centre-x anchor** | **1.1 / 0.38** | **1.8 / 0.38** | **2.0 / 0.45** | **best** |
| E2 uniform s = W/1723, centre-x anchor | 10.8 / 5.20 | 12.5 / 6.03 | 16.1 / 7.68 | rejected |
| E3 uniform s = H/1009, right anchor | 15.3 / 7.04 | 16.7 / 7.87 | 22.9 / 10.6 | rejected |
| **G** Unity-style canvas scaler, free match m and anchor a | best m = **1.00** (RMS ≤ 0.5 for m ≈ 0.95–1.05), a = **0.50 W** (RMS ≤ 0.5 for a ≈ 0.48–0.52) | | | **reduces to E** |
| **F** breakpoint/preset by aspect or width | no switch seen between aspect 1.708 and 1.778, or between 720 and 1080 px height | | | **not supported in the tested range; untestable below 1.7 here** |

Every residual above 1.1 px belongs to the legendary-frame card's top/height. **CONFIRMED (measurement).**

### 20.4 Best-fit model (E)

For display index *i* in Arena-DB order, `c = i mod 5`, `r = ⌊i / 5⌋`. Three equivalent forms:

```
(1) s = H / 1009
    x(c) = W/2 + s · (−634.49 + 183.254·c)      y(r) = s · (175.07 + 250.016·r)
    w    = s · 158.51                          h    = s · 216.75

(2) in units of client height
    x(c) = W/2 + H · (−0.62883 + 0.18162·c)     y(r) = H · (0.17351 + 0.24779·r)
    w    = 0.15710·H                           h    = 0.21482·H

(3) 1080-p reference (s' = H / 1080)
    x(c) = W/2 + s' · (−679.1 + 196.15·c)       y(r) = s' · (187.4 + 267.6·r)
    card = s' · (169.7 × 232.0)
```

Rows fill left to right and the last row stays left-aligned. Uniform scale by client height plus a horizontal-centre anchor is what a Unity canvas set to "match height" produces. That Arena actually uses such a scaler is **INFERENCE**; the formula itself is **CONFIRMED** in the tested range.

Implication: at narrower aspects the grid's left edge (W/2 − 0.629·H) approaches the client edge (x = 0.028·W at 4:3) and would leave it below aspect ≈1.26. Arena therefore must switch layout or scaling somewhere below 16:10. **INFERENCE; untested.**

### 20.5 Out-of-sample validation at 1920×1080 (pre-registered)

Parameters were fixed before the change (fitted on 1723×1009 ×2 + 1280×720).

| Slot | Measured x, y, w, h | Pre-registered prediction x, y, w, h | Residual dx, dy, dw, dh |
|---|---|---|---|
| r0c0 | 281, 189, 170, 230 | 280.8, 187.4, 169.5, 232.1 | +0.2, +1.6, +0.5, -2.1 |
| r0c1 | 477, 187, 170, 232 | 477.0, 187.4, 169.5, 232.1 | -0.0, -0.4, +0.5, -0.1 |
| r0c3 | 869, 187, 170, 232 | 869.4, 187.4, 169.5, 232.1 | -0.4, -0.4, +0.5, -0.1 |
| r0c4 | 1065, 187, 170, 232 | 1065.7, 187.4, 169.5, 232.1 | -0.7, -0.4, +0.5, -0.1 |
| r1c0 | 281, 455, 170, 232 | 280.8, 454.9, 169.5, 232.1 | +0.2, +0.1, +0.5, -0.1 |
| r1c1 | 477, 455, 170, 232 | 477.0, 454.9, 169.5, 232.1 | -0.0, +0.1, +0.5, -0.1 |
| r1c2 | 673, 455, 170, 232 | 673.2, 454.9, 169.5, 232.1 | -0.2, +0.1, +0.5, -0.1 |
| r1c3 | 869, 455, 170, 232 | 869.4, 454.9, 169.5, 232.1 | -0.4, +0.1, +0.5, -0.1 |
| r1c4 | 1065, 455, 170, 232 | 1065.7, 454.9, 169.5, 232.1 | -0.7, +0.1, +0.5, -0.1 |
| r2c0 | 281, 723, 170, 232 | 280.8, 722.5, 169.5, 232.1 | +0.2, +0.5, +0.5, -0.1 |
| r2c1 | 477, 723, 170, 232 | 477.0, 722.5, 169.5, 232.1 | -0.0, +0.5, +0.5, -0.1 |
| r2c2 | 673, 723, 170, 232 | 673.2, 722.5, 169.5, 232.1 | -0.2, +0.5, +0.5, -0.1 |


Out-of-sample: max |residual| 2.1 px, RMS 0.51 px over 12 cards x 4 coordinates.

Leaving out the legendary-frame card's top/height, **every coordinate is within 0.7 px.** **EXPERIMENTAL (n = 12 cards, one size).**

### 20.6 Per-slot residuals, joint fit (model E, all 49 cards)

| Client / pack | Slot | Measured x, y, w, h | Model E x, y, w, h | Residual dx, dy, dw, dh |
|---|---|---|---|---|
| 1723×1009 P1P2 | r0c0 | 227.0, 176.0, 159.0, 215.0 | 227.0, 175.1, 158.5, 216.8 | -0.0, +0.9, +0.5, -1.8 |
| 1723×1009 P1P2 | r0c1 | 410.0, 175.0, 159.0, 216.0 | 410.3, 175.1, 158.5, 216.8 | -0.3, -0.1, +0.5, -0.8 |
| 1723×1009 P1P2 | r0c3 | 777.0, 175.0, 158.0, 217.0 | 776.8, 175.1, 158.5, 216.8 | +0.2, -0.1, -0.5, +0.2 |
| 1723×1009 P1P2 | r0c4 | 960.0, 175.0, 158.0, 216.0 | 960.0, 175.1, 158.5, 216.8 | -0.0, -0.1, -0.5, -0.8 |
| 1723×1009 P1P2 | r1c0 | 227.0, 425.0, 159.0, 217.0 | 227.0, 425.1, 158.5, 216.8 | -0.0, -0.1, +0.5, +0.2 |
| 1723×1009 P1P2 | r1c1 | 410.0, 425.0, 159.0, 217.0 | 410.3, 425.1, 158.5, 216.8 | -0.3, -0.1, +0.5, +0.2 |
| 1723×1009 P1P2 | r1c2 | 594.0, 425.0, 158.0, 217.0 | 593.5, 425.1, 158.5, 216.8 | +0.5, -0.1, -0.5, +0.2 |
| 1723×1009 P1P2 | r1c3 | 777.0, 425.0, 158.0, 217.0 | 776.8, 425.1, 158.5, 216.8 | +0.2, -0.1, -0.5, +0.2 |
| 1723×1009 P1P2 | r1c4 | 960.0, 425.0, 158.0, 217.0 | 960.0, 425.1, 158.5, 216.8 | -0.0, -0.1, -0.5, +0.2 |
| 1723×1009 P1P2 | r2c0 | 227.0, 675.0, 159.0, 217.0 | 227.0, 675.1, 158.5, 216.8 | -0.0, -0.1, +0.5, +0.2 |
| 1723×1009 P1P2 | r2c1 | 410.0, 675.0, 159.0, 217.0 | 410.3, 675.1, 158.5, 216.8 | -0.3, -0.1, +0.5, +0.2 |
| 1723×1009 P1P2 | r2c2 | 594.0, 675.0, 158.0, 217.0 | 593.5, 675.1, 158.5, 216.8 | +0.5, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r0c1 | 410.0, 175.0, 159.0, 217.0 | 410.3, 175.1, 158.5, 216.8 | -0.3, -0.1, +0.5, +0.2 |
| 1723×1009 P1P1 | r0c2 | 594.0, 175.0, 158.0, 217.0 | 593.5, 175.1, 158.5, 216.8 | +0.5, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r0c3 | 777.0, 175.0, 158.0, 217.0 | 776.8, 175.1, 158.5, 216.8 | +0.2, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r0c4 | 960.0, 175.0, 158.0, 217.0 | 960.0, 175.1, 158.5, 216.8 | -0.0, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r1c0 | 227.0, 425.0, 159.0, 217.0 | 227.0, 425.1, 158.5, 216.8 | -0.0, -0.1, +0.5, +0.2 |
| 1723×1009 P1P1 | r1c1 | 410.0, 425.0, 159.0, 217.0 | 410.3, 425.1, 158.5, 216.8 | -0.3, -0.1, +0.5, +0.2 |
| 1723×1009 P1P1 | r1c2 | 594.0, 425.0, 158.0, 217.0 | 593.5, 425.1, 158.5, 216.8 | +0.5, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r1c3 | 777.0, 425.0, 158.0, 217.0 | 776.8, 425.1, 158.5, 216.8 | +0.2, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r1c4 | 960.0, 425.0, 158.0, 217.0 | 960.0, 425.1, 158.5, 216.8 | -0.0, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r2c0 | 227.0, 675.0, 159.0, 217.0 | 227.0, 675.1, 158.5, 216.8 | -0.0, -0.1, +0.5, +0.2 |
| 1723×1009 P1P1 | r2c1 | 410.0, 675.0, 159.0, 217.0 | 410.3, 675.1, 158.5, 216.8 | -0.3, -0.1, +0.5, +0.2 |
| 1723×1009 P1P1 | r2c2 | 594.0, 675.0, 158.0, 217.0 | 593.5, 675.1, 158.5, 216.8 | +0.5, -0.1, -0.5, +0.2 |
| 1723×1009 P1P1 | r2c3 | 777.0, 675.0, 158.0, 217.0 | 776.8, 675.1, 158.5, 216.8 | +0.2, -0.1, -0.5, +0.2 |
| 1280×720 P1P2 | r0c0 | 187.0, 125.0, 113.0, 154.0 | 187.2, 124.9, 113.1, 154.7 | -0.2, +0.1, -0.1, -0.7 |
| 1280×720 P1P2 | r0c1 | 318.0, 125.0, 113.0, 154.0 | 318.0, 124.9, 113.1, 154.7 | -0.0, +0.1, -0.1, -0.7 |
| 1280×720 P1P2 | r0c3 | 580.0, 125.0, 112.0, 154.0 | 579.5, 124.9, 113.1, 154.7 | +0.5, +0.1, -1.1, -0.7 |
| 1280×720 P1P2 | r0c4 | 710.0, 125.0, 113.0, 154.0 | 710.3, 124.9, 113.1, 154.7 | -0.3, +0.1, -0.1, -0.7 |
| 1280×720 P1P2 | r1c0 | 187.0, 303.0, 113.0, 155.0 | 187.2, 303.3, 113.1, 154.7 | -0.2, -0.3, -0.1, +0.3 |
| 1280×720 P1P2 | r1c1 | 318.0, 303.0, 113.0, 155.0 | 318.0, 303.3, 113.1, 154.7 | -0.0, -0.3, -0.1, +0.3 |
| 1280×720 P1P2 | r1c2 | 449.0, 303.0, 113.0, 155.0 | 448.8, 303.3, 113.1, 154.7 | +0.2, -0.3, -0.1, +0.3 |
| 1280×720 P1P2 | r1c3 | 580.0, 303.0, 112.0, 155.0 | 579.5, 303.3, 113.1, 154.7 | +0.5, -0.3, -1.1, +0.3 |
| 1280×720 P1P2 | r1c4 | 710.0, 303.0, 113.0, 155.0 | 710.3, 303.3, 113.1, 154.7 | -0.3, -0.3, -0.1, +0.3 |
| 1280×720 P1P2 | r2c0 | 187.0, 482.0, 113.0, 155.0 | 187.2, 481.7, 113.1, 154.7 | -0.2, +0.3, -0.1, +0.3 |
| 1280×720 P1P2 | r2c1 | 318.0, 482.0, 113.0, 155.0 | 318.0, 481.7, 113.1, 154.7 | -0.0, +0.3, -0.1, +0.3 |
| 1280×720 P1P2 | r2c2 | 449.0, 482.0, 113.0, 155.0 | 448.8, 481.7, 113.1, 154.7 | +0.2, +0.3, -0.1, +0.3 |
| 1920×1080 P1P2 | r0c0 | 281.0, 189.0, 170.0, 230.0 | 280.9, 187.4, 169.7, 232.0 | +0.1, +1.6, +0.3, -2.0 |
| 1920×1080 P1P2 | r0c1 | 477.0, 187.0, 170.0, 232.0 | 477.0, 187.4, 169.7, 232.0 | -0.0, -0.4, +0.3, -0.0 |
| 1920×1080 P1P2 | r0c3 | 869.0, 187.0, 170.0, 232.0 | 869.3, 187.4, 169.7, 232.0 | -0.3, -0.4, +0.3, -0.0 |
| 1920×1080 P1P2 | r0c4 | 1065.0, 187.0, 170.0, 232.0 | 1065.5, 187.4, 169.7, 232.0 | -0.5, -0.4, +0.3, -0.0 |
| 1920×1080 P1P2 | r1c0 | 281.0, 455.0, 170.0, 232.0 | 280.9, 455.0, 169.7, 232.0 | +0.1, -0.0, +0.3, -0.0 |
| 1920×1080 P1P2 | r1c1 | 477.0, 455.0, 170.0, 232.0 | 477.0, 455.0, 169.7, 232.0 | -0.0, -0.0, +0.3, -0.0 |
| 1920×1080 P1P2 | r1c2 | 673.0, 455.0, 170.0, 232.0 | 673.2, 455.0, 169.7, 232.0 | -0.2, -0.0, +0.3, -0.0 |
| 1920×1080 P1P2 | r1c3 | 869.0, 455.0, 170.0, 232.0 | 869.3, 455.0, 169.7, 232.0 | -0.3, -0.0, +0.3, -0.0 |
| 1920×1080 P1P2 | r1c4 | 1065.0, 455.0, 170.0, 232.0 | 1065.5, 455.0, 169.7, 232.0 | -0.5, -0.0, +0.3, -0.0 |
| 1920×1080 P1P2 | r2c0 | 281.0, 723.0, 170.0, 232.0 | 280.9, 722.6, 169.7, 232.0 | +0.1, +0.4, +0.3, -0.0 |
| 1920×1080 P1P2 | r2c1 | 477.0, 723.0, 170.0, 232.0 | 477.0, 722.6, 169.7, 232.0 | -0.0, +0.4, +0.3, -0.0 |
| 1920×1080 P1P2 | r2c2 | 673.0, 723.0, 170.0, 232.0 | 673.2, 722.6, 169.7, 232.0 | -0.2, +0.4, +0.3, -0.0 |

### 20.7 Untapped badges vs the slot formula

Untapped's strips and shields were measured against the **formula-predicted** slots, using no Arena pixels. Offsets are divided by s (reference units); medians, with ranges in brackets.

| Client | Strip dx | Strip dy | Strip width | Shield centre dx | Shield bottom − (slot y + h) |
|---|---|---|---|---|---|
| 1723×1009 | +6.1 [6.0…6.7] | +19.9 [19.9] | 146.0 [145.0…147.0] | −0.1 [−0.5…+1.2] | +21.7 [21.2…24.2] |
| 1280×720 | +6.7 [6.3…11.5¹] | +20.3 [19.7…21.4] | 145.0 [141.5…145.7] | −0.1 [−0.5…+2.3²] | +21.7 [21.0…23.8] |
| 1920×1080 | +6.7 [6.3…6.7] | +20.6 [20.2…20.9] | 144.8 [144.8…145.7] | −0.3 [−1.1…+2.5²] | +21.7 [21.1…24.3] |

¹ A partly isolated strip. ² The narrow "11" shield art (Fell Horseman) reads +1.5…3 px at every size, so it comes from the art, not placement. The shield-bottom spread (21–24) follows the flame art.

- **Untapped's badge geometry equals model E's slot geometry plus fixed offsets ×s, to within about 1 px at all three sizes.** CONFIRMED (measurement).
- Untapped's overlay also followed every client change (1723×1009 → 1366×768 → 1280×720 → 1920×1080).
- **This does not show that Untapped and Arena share an implementation.** Untapped could evaluate an equivalent formula or read Arena's layout from memory; both give identical placements at every size tested. **INFERENCE either way; unresolved.** A regime where Arena changes layout (narrow aspect, ≤5 cards) is the place where the two could diverge.

### 20.8 Confidence

| Claim | Label | Confidence |
|---|---|---|
| Arena's draft grid scales uniformly with client height and is centred horizontally (formula 20.4) | CONFIRMED in range: aspect 1.708–1.778, heights 720–1080 (plus a ×1.5 DPI rescale at 1516) | High |
| Same grid for 13 and 14 cards; last row left-aligned | CONFIRMED | High |
| Behaviour at aspect < 1.7, heights < 720 or > 1516, ≤5 cards, 15 cards, fullscreen | Untested | — |
| Arena uses a "match height" canvas scaler | INFERENCE | Moderate |
| Untapped computes the same formula (vs reading Arena's layout) | INFERENCE / unresolved | Low–moderate |

### 20.9 Is fixed-slot geometry strong enough for a DraftTG prototype?

**Yes, for a prototype, with conditions.** Within the validated regime, a pure function of client size and pack index reproduces Arena's card rectangles to ≤1 px (RMS 0.4 px). A prediction made before the measurement held at a new size. That is better than any image-based placement needs, and Untapped ships the same placement in production. Conditions for the prototype (design guidance only; nothing implemented):

1. **Order and identity:** Player.log pack plus the Arena-DB order model. Use only *unanimous* order predictions; otherwise fall back to the existing visual path.
2. **Geometry:** formula 20.4, from the live client rect. Recompute on every move, size or DPI change.
3. **Verification gate:** before showing badges for a pack, spot-check one or two predicted slots against a WGC frame (holder colour around the predicted rect, or the existing artwork matcher). Fail closed to the current visual locator.
4. **Regime guard:** if aspect < 1.70 or > 1.78, height < 720 (or > 1516), card count ≤ 5 or ≥ 15, or Arena is fullscreen, use the visual path until that regime is measured. The guard can widen as §19 experiments land.
5. **Fixture:** the measured grids (`grid-*.json`, no personal data) can become test fixtures for the formula.

## 21. Audit housekeeping

| Item | Finding | Recommendation |
|---|---|---|
| Stray file `tatus` (repo root) | Committed in HEAD `5fb2b2c` "Add order evidence and Arena card identity fallback" (2026-10-07 07:19). It contains 24 lines of ANSI-coloured `git diff --stat` output plus a CRLF warning, apparently an accidental redirect of a `git` command. No tracked file references it, and no project, solution or glob includes repo-root files. | **Safe to remove** with `git rm tatus` in its own commit. Not removed and nothing committed during this audit. |
| Raw artifacts `artifacts/untapped-audit/` | **252 MB**, ignored by `.gitignore` (`artifacts/`, verified with `git check-ignore`). ~250 MB is screenshots/frames that show the Arena screen name. ~1.1 MB is JSON/log/text; the grid, badge and fit outputs contain no personal identifiers. | **Do not commit.** Every final measurement is now in this report. Delete the image folders (`A-baseline/*.png`, `B-foreground`, `J-L-move-hover`, `M-pick`, `G-geometry/*.png`, `G-geometry/validation/*.png`) once you've reviewed it. Keep the ~1 MB of JSON/text only if you want to re-run the fits; the `grid-*.json` files are the useful seed for a future fixture. Untapped's own logs were never copied into artifacts. |
| Audit tool | `tools/UntappedLocalizationAudit` (untracked; not in `DraftTG.sln`) builds with 0 warnings. | Keep as a research tool, or commit together with this report. |

---

### Method and safety notes

- **Read-only throughout.** No injection, hooking, patching, memory writes, memory dumps, TLS interception, credential access, or modification of Arena/Untapped files.
- **Handle identification** duplicated Untapped's handles into the audit process only to call `GetProcessId` / `GetFinalPathNameByHandle`, then closed them. A duplicated Arena handle briefly carried Untapped's `VM_READ` right; it was never used to read memory.
- **`arena-modules`** enumerated MTGA's loaded modules (standard module enumeration) to look for injected DLLs.
- **UIA queries** may switch on Chromium accessibility in the overlay renderer. This is a benign side effect.
- **Static analysis** was limited to file names, archive metadata and presence counts of API/keyword strings. No proprietary code or recommendation logic was read, reproduced or reconstructed.
- **User actions:** every window change and the single pick were performed by the user on request.
- **Privacy:** account identifiers, e-mail, screen names, opponent names and draft IDs were seen in local logs/screens and are deliberately omitted here.
