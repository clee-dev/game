# Wiring: Steel Material & Welding Torch — Two-Person Carry + Burnout

**Status:** Code complete (`WeightClass`, `TwoPersonCarry`, `TwoPersonCarryPoint`,
`WeldingTorchFuel`, plus the `PlayerController`/`PlayerInteraction`/`BuildTile`
integration — see `docs/ARCHITECTURE.md` Player / Interaction & Pickup / Build
Tiles sections). Editor wiring partially done already (see below), partially
still needed.

**Supersedes the Welding Torch portion of `docs/wiring/trowel-and-torch-tool-prefabs.md`.**
That doc assumed Welding Torch was a simple instant-build tool identical to
Hammer (just a prefab + `ToolItem.toolType`). It now also needs a
`WeldingTorchFuel` component and is driven by a stillness-pause build rule, not
a plain timer — see "Welding Torch.prefab" below. Per project convention,
`trowel-and-torch-tool-prefabs.md` itself is left unmodified; its Trowel
portion is still accurate and unaffected.

---

## What already exists — no action needed

Cameron's own earlier Editor session (commits `d52e5f9`, `2d185a3`) already
built and registered base prefabs for all four Phase A materials/tools, ahead
of any of their gameplay code:

- `Steel.prefab` — `Rigidbody`, `BoxCollider`, `NetworkObject`,
  `ClientNetworkTransform` (Authority Mode = Owner ✓, matches
  `WoodPlank`/`Hammer`), `PhysicsPickup`, `MaterialItem` (`materialType =
  Steel` ✓). Registered in `Assets/DefaultNetworkPrefabs.asset`.
- `Welding Torch.prefab` — same base layout, `ToolItem` (`toolType = Torch`
  ✓). Registered in `Assets/DefaultNetworkPrefabs.asset`.
- `Concrete.prefab` / `Trowel.prefab` — same pattern, correctly typed,
  registered. Not part of this feature (Concrete is still Phase A future
  work) — listed only because they were built in the same pass as Steel/Torch.
- The **master** `Assets/Prefabs/ToolDepotSpawner.prefab` template's
  `toolPrefabs[]` array already contains Hammer, Trowel, **and** Welding
  Torch. Any blueprint tool depot whose `tools` JSON array lists `"torch"` or
  `"trowel"` will already resolve a prefab via `ToolDepotSpawner.Configure()`
  — no code or prefab change needed for a depot to offer the Torch once a
  blueprint asks for it.

None of the above needs to be touched for this feature.

---

## What's new since those prefabs were built

The new components below don't exist on `Steel.prefab` / `Welding
Torch.prefab` yet, because the scripts didn't exist when Cameron built them.
`PhysicsPickup` also gained a `weightClass` field this session — it isn't
serialized on any existing prefab yet, so every prefab (including `Steel`)
currently shows/defaults to `Light` until set otherwise in the Inspector.

### `Steel.prefab` — add:

1. **`TwoPersonCarry`** component on the root (sibling of `PhysicsPickup` /
   `NetworkObject`). Leave `forwardOffset` (0.6) / `heightOffset` (1.2) at
   their script defaults unless they look wrong once you can see it in-game.
2. **`PhysicsPickup.weightClass` → `Heavy`.** This is the actual 75% speed
   penalty switch — currently unset (defaults to `Light`, i.e. no penalty).
3. **A new child GameObject for the attach point** — sibling of the existing
   `Cube` child, *not* the same object (`TwoPersonCarryPoint`'s own doc
   comment requires a Collider separate from the main pickup Collider, so the
   raycast can resolve "grab the beam" vs. "help carry the beam" as two
   distinct targets). Give it its own small `Collider` (e.g. a `BoxCollider`
   near one end of the beam mesh) plus a `TwoPersonCarryPoint` component whose
   `carry` field points back at the `TwoPersonCarry` on the prefab root.

### `Welding Torch.prefab` — add:

1. **`WeldingTorchFuel`** component on the root (sibling of `ToolItem` /
   `NetworkObject`). Defaults (`maxHeat` 8s, `drainRate` 1/sec,
   `cooldownDuration` 4s) match the placeholders in
   `docs/PLANNED_FEATURES.md` — retune to taste, nothing else needed.

### `Player.prefab` — add:

1. **`PlayerController.playerInteraction`** → assign to the `PlayerInteraction`
   component already on the Player root (the same component
   `NetworkPlayer.playerInteraction` already points at — look for the
   `PlayerInteraction` entry in the component list, it's the one with
   `playerCamera`/`mouseLook`/`holdPoint`/`interactPrompt` fields). This is
   the only `Player.prefab` change needed for the weight system to turn on —
   `mediumWeightMultiplier` (0.85) / `heavyWeightMultiplier` (0.25) already
   default sensibly in code and don't need Inspector overrides unless you
   want to retune the feel.

---

## Flag for Cameron — supply zone currently spawns Steel, not Wood

`Assets/Prefabs/SupplyZoneSpawner.prefab` — the exact prefab
`Game1.unity`'s `BuildSystem.supplyZoneSpawnerPrefab` field points to — has
its single `materialPrefab` field set to **Steel**, not Wood. I didn't change
this; it's already committed (one of the same commits that added the Steel
prefab). Flagging rather than flipping it back myself, since I don't know if
this was deliberate mid-test state or a leftover.

This also exposes a real limitation, not just a misconfigured value:
`SupplyZoneSpawner` has no per-instance material selection (its blueprint
JSON entries are just `id` + `worldPosition`, no material field), and
`BuildSystem` holds exactly one `supplyZoneSpawnerPrefab` for the whole scene.
So **every** supply zone in a level currently spawns whatever that one
prefab's `materialPrefab` is, and **Wood and Steel supply zones can't coexist
in the same blueprint today**. If you want a level with both, `SupplyZoneSpawner`
would need the same treatment `ToolDepotSpawner` already got — an array +
`Configure()` keyed by material, with each supply zone's JSON entry picking
one. Not attempted here since it's a separate architectural change, not part
of what was asked for this pass.

---

## Blueprint content (Cameron, content-authoring)

No blueprint currently places a Steel tile, asks a tool depot for `"torch"`,
or (per the flag above) has a working path to a Steel supply zone. Tiles are
fully data-driven already — each tile JSON entry has its own
`requiredMaterial`/`requiredTool` strings (e.g. `"steel"` / `"torch"`), so
adding Steel tiles to a blueprint needs no code changes, just JSON content
(or the Level Editor, once it supports picking Steel/Torch — not checked as
part of this pass). To actually play the loop end to end:

- Add one or more tiles with `"requiredMaterial": "steel"`,
  `"requiredTool": "torch"` to a blueprint.
- Resolve the supply-zone flag above (decide Wood vs. Steel, or build the
  multi-material extension) so a Steel beam can actually reach the tile.
- Add `"torch"` to a tool depot's `tools` array in the same blueprint (the
  master `ToolDepotSpawner.prefab` already has a Torch prefab ready to go,
  per "What already exists" above).

---

## Suggested playtest once the above is wired

- Solo-carry a Steel beam: confirm movement drops to ~25% speed.
- Second player binds at the attach point: confirm both players return to
  full speed, and the prompt reads "[E] Help Carry" / "[E] Let Go (Carrying)"
  correctly for each role.
- Either carrier releases independently: secondary releasing leaves the
  primary still carrying; primary releasing while shared hands the beam to
  the secondary instead of dropping it.
- Weld a Steel tile: moving while welding pauses progress (bar doesn't drop),
  not resets it; standing still resumes it; ~8s of continuous welding
  overheats the torch (heat bar in `OnGUI` goes red, "Overheated" label) and
  locks it out for ~4s before it can heat again.
