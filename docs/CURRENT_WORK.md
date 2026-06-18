# Current Work — Order Menu: Mouse Unlock + Clickable Buttons

Scoped doc for the thing in progress right now. Delete/clear this once it's wired and
tested — permanent history goes in `docs/CHANGELOG.md` instead.

## What's done in code

- `PlayerCamera.SetLookEnabled(bool)` — new public method. `false` stops mouse-look
  rotation and unlocks/shows the OS cursor; `true` re-locks/hides it and resumes look.
  Same lock/unlock behavior the pause menu already uses, just callable on demand instead
  of only through `GameEvents`.
- `PlayerInteraction` has a new `mouseLook` field (type `PlayerCamera`) and calls
  `SetLookEnabled` automatically when the order menu opens/closes. Opening happens on
  `E` (unchanged), closing happens on `E` again, looking away, or placing an order.
- `PlayerInteraction` exposes for UI Buttons to call:
  - `SelectOrderOption(int index)` — places the order for `availableMaterials[index]` on
    whichever `OrderStation` menu is currently open, then closes the menu. Same code path
    the number keys already use, so a click and a keypress do exactly the same thing.
  - `CloseOrderMenu()` — dismiss without ordering (wire to a Cancel button).
  - `IsOrderMenuOpen` (bool) and `OpenOrderMenuTarget` (OrderStation) — read these if your
    UI script wants to show/hide the panel and label buttons dynamically via
    `OpenOrderMenuTarget.MaterialCount` / `OpenOrderMenuTarget.DescribeOption(i)` instead
    of hardcoding text.
- Number-key selection (1–9) still works, unchanged — this adds a second input path, not
  a replacement.

## Editor steps (yours)

1. **`Player.prefab` → `PlayerInteraction` → assign `mouseLook`**: drag the same
   **Main Camera** child you used for `playerCamera`. Unity will pick up its
   `PlayerCamera` script component this time since the field type is different — don't
   reuse the `playerCamera` Camera reference itself, wrong type.
2. **Build the menu UI inside `Player.prefab`**, as a child of the existing `Canvas`
   object (the one `InteractPrompt` already lives under) — not as a separate scene-level
   Canvas. This matters: each Button's `OnClick` needs to target *this specific player's*
   `PlayerInteraction` component, which only resolves correctly per-instance if the UI is
   part of the same prefab (same reason `interactPrompt` is wired this way already). A
   Canvas built elsewhere in the scene has no reliable way to reference "the local
   player," since that object is spawned at runtime over the network.
3. **Per material-option button**: `OnClick` → drag the prefab's root `Player` object →
   pick `PlayerInteraction.SelectOrderOption(int)` from the function dropdown. Picking a
   single-argument method like this makes Unity show an inline int field — set it to that
   option's index (0 for the first material, 1 for the second, etc.). Optional Cancel
   button → same target → `PlayerInteraction.CloseOrderMenu()`.
4. **Panel visibility**: toggle your menu panel's `GameObject` active/inactive based on
   `PlayerInteraction.IsOrderMenuOpen` (e.g. a small script polling it in `Update()`).
   Start it inactive.
5. **`Game1.unity` needs an `EventSystem`** (GameObject → UI → Event System) — it doesn't
   have one yet. `Hub.unity` and `MainMenu.unity` already do, which is why their menus
   accept clicks and this scene's wouldn't yet. Unity auto-attaches
   `InputSystemUIInputModule` to match this project's Input System setup (same as
   Hub/MainMenu) — don't manually swap it for the legacy Standalone Input Module.
6. The existing `Canvas` on `Player.prefab` is already Screen Space – Overlay with a
   `GraphicRaycaster`, so nothing to change there — step 5 (scene `EventSystem`) is the
   only missing piece for clicks to register at all.

## Test

- Open the menu (`E` on an `OrderStation`) → cursor appears, camera stops following
  mouse movement.
- Click a button → order placed, menu closes, cursor re-locks, camera look resumes.
- Number keys 1–9 still work and behave identically to clicking.
- Looking away from the station while the menu is open closes it and re-locks the
  cursor.

Once this is wired and tested, tell me and I'll remove this file.
