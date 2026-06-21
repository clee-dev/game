# Wiring: Kiosk & Terminal Canvas Menus

**Status: DONE.** Built and wired programmatically via `Unity_RunCommand`
(`UnityEditor.Events.UnityEventTools.AddIntPersistentListener`/
`AddVoidPersistentListener` against a loaded `PrefabUtility.LoadPrefabContents`
instance of `Player.prefab`, saved back with `PrefabUtility.SaveAsPrefabAsset`).
The initial assumption below — that `Button.OnClick` can't be wired through
automated tooling — was correct for `Unity_ManageGameObject.set_component_property`
(confirmed: silently no-ops on UnityEvent fields) but **not** for direct
Editor-script execution, which has full access to `UnityEventTools`. Verified
after building: both panels exist, both `*MenuPanel` components have all
serialized references populated, and `Button.onClick.GetPersistentEventCount()`
returns 1 with the correct target method name for both Kiosk and Terminal rows.
The OnGUI fallback (`DrawKioskMenu`/`DrawTerminalMenu`) has been removed from
`PlayerInteraction.cs`. Layout (anchored positions/sizes below) was set
programmatically and not visually proofed by a human — Cameron should open the
Hub kiosk/terminal in-Editor once and confirm it reads well, then retune
spacing/styling directly in the Inspector if needed (now that it's real UI,
not OnGUI, that's a normal Editor task rather than a code change).

The rest of this document is left as-is below for reference on what was built
and why, in case it needs rebuilding or extending.

---

**Original status note (superseded above):** Editor-only wiring — code is
complete (`KioskMenuPanel.cs`, `TerminalMenuPanel.cs`, both new), but
`Button.OnClick` persistent listeners and array-of-reference Inspector fields
can't be wired through automated tooling — they need to be dragged in the
Editor.

## Why this exists

`Player.prefab` already has a working Canvas-based Order menu: a `menuUI` panel
with 3 hardcoded buttons (Wood/Concrete/Steel) wired to
`PlayerInteraction.SelectOrderOption(int)`, toggled on/off by `OrderMenuPanel.cs`
reading `PlayerInteraction.IsOrderMenuOpen`. This session found and removed a
duplicate-rendering bug where `PlayerInteraction.OnGUI()` was *also* drawing an
old immediate-mode order menu on top of those buttons every time the menu
opened — that's fixed, no further action needed there.

Kiosk (`LevelSelectKiosk`) and Terminal (`HubTerminal`) still render via OnGUI
(`PlayerInteraction.DrawKioskMenu()` / `DrawTerminalMenu()`, intentionally left
in place as a working fallback) because they don't have a Canvas equivalent
yet. This task brings them onto the same pattern as Order, for visual/code
consistency project-wide. Once both are wired and confirmed working in-Editor,
delete `DrawKioskMenu()`/`DrawTerminalMenu()` and their `OnGUI()` calls in
`PlayerInteraction.cs` (same as `DrawOrderMenu()` was deleted this session).

**Why 9 fixed rows, not a dynamic list:** `PlayerInteraction`'s
`HandleKioskMenuSelection`/`HandleTerminalMenuSelection` already cap selectable
options at `DigitKeys.Length` (9, number keys 1-9) — anything beyond that is
already unreachable today regardless of UI. A panel with 9 pre-built rows
(active/inactive toggled per-frame) reuses the same shape as the Order menu's 3
fixed buttons instead of introducing a new dynamic-instantiation system.

## What to build

The fastest path is to **duplicate the existing Order menu structure** and
relabel, since it's already proven and styled correctly.

### 1. Kiosk panel

On `Player.prefab`, under the existing `Canvas` GameObject (sibling of
`menuUI`):

1. Duplicate `menuUI` → rename to `kioskMenuUI`.
2. It currently has 4 children (Wood/Concrete/Steel buttons + a Back button).
   Duplicate the button template until you have **9** option buttons, plus
   keep the existing Back-style button as Cancel (wire its `OnClick` to
   `PlayerInteraction.CloseKioskMenu()`, no argument).
3. For each of the 9 option buttons: wire `Button.OnClick` →
   `PlayerInteraction.SelectKioskOption(int)` with the static argument set to
   the row's index (0-8) — same pattern as the Order buttons' `SelectOrderOption`
   wiring, just a different target method.
4. Set each button's child TMP label to placeholder text (e.g. "Option") —
   `KioskMenuPanel` overwrites it at runtime, the placeholder is just so it's
   not blank in the Inspector.
5. Add a `KioskMenuPanel` component to the `Canvas` GameObject (alongside the
   existing `OrderMenuPanel`). Assign:
   - `playerInteraction` → the `PlayerInteraction` component on `Player` root
     (same reference `OrderMenuPanel.playerInteraction` already uses).
   - `panel` → `kioskMenuUI`.
   - `rows` → the 9 option button GameObjects, in index order.
   - `rowLabels` → each button's child `TextMeshProUGUI`, same order as `rows`.

### 2. Terminal panel

Same duplication approach, but Terminal needs more per-row content:

1. Duplicate `kioskMenuUI` (once built) → rename to `terminalMenuUI`.
2. Each of the 9 rows needs **two** text lines instead of one: the existing
   label (option name) plus a second, smaller TMP text underneath for
   `HubTerminal.DescribeDetails(i)` (tile count / materials / completion
   threshold).
3. Add one shared `RawImage` (not per-row) for the top-down preview texture —
   `HubTerminal.GetPreviewTexture(int)` returns a `Texture2D` for whichever row
   is currently highlighted.
4. Add one TMP label, inactive by default, for the "Selection confirmed!" flash
   message.
5. Each row button's `OnClick` → `PlayerInteraction.ConfirmTerminalOption(int)`
   with the row index. **Judgment call, flag for Cameron:** this means
   clicking a row both highlights *and* confirms in one click, whereas the
   keyboard path is two steps (number key to highlight, Enter to confirm).
   Reasonable for a single click-driven UI, but different from the keyboard
   flow — change to a highlight-only `OnClick` if you'd rather keep them
   symmetric (there's no existing "highlight without confirming" public method
   on `PlayerInteraction` for that — would need a small new one,
   e.g. `HighlightTerminalOption(int)`, that just sets `_terminalHighlightedIndex`).
6. Add a `TerminalMenuPanel` component to the `Canvas` GameObject. Assign:
   - `playerInteraction`, `panel` (→ `terminalMenuUI`) — same as Kiosk.
   - `rows` (9 row GameObjects), `rowLabels` (9 option-name TMPs), `rowDetails`
     (9 detail-line TMPs) — all in matching index order.
   - `previewImage` → the shared `RawImage`.
   - `confirmedLabel` → the "Selection confirmed!" label GameObject.

## Cleanup once both are confirmed working

In `Assets/Scripts/PlayerInteraction.cs`:
- Delete `DrawKioskMenu()`, `DrawTerminalMenu()`, and their calls in `OnGUI()`.
- Delete the now-unused `MenuWidth`/`MenuLineHeight`/`MenuPadding`/
  `TerminalMenuWidth`/`TerminalDetailLineHeight`/`TerminalPreviewSize`
  constants (check `DrawOrderQueue()`'s `Queue*` constants are untouched —
  those are separate and still needed).

## Dependencies

Blocked on: nothing. `KioskMenuPanel.cs`/`TerminalMenuPanel.cs` are
code-complete and compiled clean (`PlayerInteraction.IsKioskMenuOpen` /
`OpenKioskMenuTarget` / `IsTerminalMenuOpen` / `OpenTerminalMenuTarget` /
`TerminalHighlightedIndex` / `TerminalFlashActive` all already exist as public
getters). Pure Editor wiring, no further code needed unless you want the
highlight/confirm split noted above.

Blocks: nothing — both menus already work correctly via the OnGUI fallback,
left in place specifically so this isn't a regression while the Canvas
versions get built.
