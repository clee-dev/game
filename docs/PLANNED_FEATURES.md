# Planned Features

Everything in this document is designed but not yet implemented. Organized by
development phase from `docs/GAME_INTENT.md`. Each entry includes what the feature
is, key decisions already made, dependencies, and open questions.

This is the product backlog. When starting work on any feature here, move it to
`docs/ARCHITECTURE.md` once it's built.

---

## Hub Systems

### Hub Terminal (Blueprint Selector)

**What it is:** A physical terminal object placed in the Hub world. Players walk up
to it to browse and select which blueprint to play next. Replaces the current
`LevelSelectKiosk` `OnGUI` numbered list with a proper world-space UI.

**Design intent:** The terminal should feel like a job board or mission terminal.
Players gather around it together, one person (or the host) makes the selection, and
everyone sees it update. It should be a natural social moment before heading to the
`StartingAreaTrigger`.

**Decisions made:**
- Host selects, or players vote (TBD — vote system is more fun but more complex)
- Selection broadcasts to all clients via `[Rpc(SendTo.Server)]`, same as
  `LevelSelectKiosk`
- Blueprint is identified by id string, not list index (list order isn't guaranteed
  to match across machines since each client scans Steam Cloud independently)
- `GameSession.SelectedBlueprintId` stores the result and is read by `BuildSystem`
  when Game1 loads

**What to build:**
- World-space Canvas on a terminal/screen prop in Hub
- Blueprint list panel: shows blueprint display name, tile count, material types
  required, completion threshold
- Selection confirmed by host (or via vote if vote system is implemented)
- NetworkVariable to sync current selection display to all clients
- Visual feedback when selection is confirmed (brief lock-in animation or color
  change)

**Dependencies:** `BlueprintLoader` (already exists), `GameSession` (already exists),
`StartingAreaTrigger` (already exists, unchanged)

**Replaces:** `LevelSelectKiosk` (`OnGUI` numbered list) — keep the old script as a
fallback but the terminal is the intended path.

**Open questions:**
- Does the host select unilaterally or do players vote?
- Should non-host players be able to browse but not confirm?
- Does the terminal show a visual preview of the blueprint grid layout?

---

### Hub Spawn Points

**What it is:** Designated spawn positions in the Hub so players don't overlap at
the origin.

**Decisions made:**
- One spawn point per player slot (up to 4)
- `HubPlayerState` assigns spawn point based on `OwnerClientId`

**What to build:**
- 4 `Transform` positions in Hub scene
- `HubSpawnPoints` component that `HubPlayerState` reads on spawn

**Dependencies:** `HubPlayerState` (already exists, already references `HubSpawnPoints`)

---

## Phase A — Complete the Material Loop

### Concrete Material

**What it is:** A medium-weight buildable material requiring two source items mixed
together before it can be placed.

**Decisions made:**
- Cement bag + water bucket are combined at a mixing point to create a concrete bag
- Concrete bag has a 20-second hardening timer once placed on a build tile
- Timer resets if a player steps on the concrete before it sets
- Weight class: Medium (minor speed penalty)
- Tool: Trowel

**What to build:**
- `CementBag`, `WaterBucket`, `ConcreteBag` prefabs (with `MaterialItem` state machine)
- `MixingPoint` interactable that combines cement + water into concrete
- Hardening timer on `BuildTile` when `MaterialType.Concrete` is placed (server-side,
  networked)
- Step-detection collision on the placed concrete (resets timer on player contact)
- `TrowelItem` prefab

**Dependencies:** `MaterialItem`, `BuildTile`, `PlayerInteraction`, `ToolItem`

**Open questions:**
- Where does the mixing point live — at supply zones, or as a fixed hub object?
- Does the hardening timer show on the build tile's progress bar canvas?
- What happens if two players try to mix at the same mixing point simultaneously?

---

### Steel Material

**What it is:** A heavy buildable material requiring two-player carry.

**Decisions made:**
- Weight class: Heavy — 75% movement speed reduction when held solo
- Two-player shared carry: second player binds in, movement averages between both,
  either can release independently
- Tool: Welding Torch (requires stillness during build interaction)
- Build duration: 4.0s (slower than Hammer/Trowel)

**What to build:**
- `SteelBeam` prefab with Heavy weight class
- Two-player carry binding system on `PlayerInteraction` / `PhysicsPickup`
- Stillness detection during Torch build (cancel progress if player moves)
- `WeldingTorch` prefab

**Dependencies:** `MaterialItem`, `PhysicsPickup`, `PlayerInteraction`, `ToolItem`,
`ToolStats`

**Open questions:**
- How does the second player "bind in" to a heavy carry? Proximity + interact prompt?
- Is the stillness threshold a velocity value or a full-stop requirement?
- Does the torch tool have a fuel/durability mechanic or is it permanent?

---

### Remaining Tools (Trowel, Welding Torch)

**Trowel:** Medium-weight tool, 2.5s build duration on Concrete tiles.
**Welding Torch:** Light-weight tool (the player carrying it is slowed — the steel
beam they're also carrying is what's heavy), 4.0s build duration, requires stillness.

Both need: prefab, `ToolItem` component, `ToolStats` values, depot spawner entry.

---

## Phase B — Game Layer

### Timer System

**What it is:** A countdown per level set by the active contract. When it hits zero,
the level ends with whatever completion percentage was reached.

**Decisions made:**
- Timer is server-authoritative, synced to all clients via `NetworkVariable<float>`
- Visible to all players (world-space HUD or screen UI)
- When timer expires, trigger same completion evaluation as manual completion

**What to build:**
- `LevelTimer` NetworkBehaviour with `NetworkVariable<float>` countdown
- Hook into `BuildSystem` completion evaluation
- Client-side display

**Dependencies:** Contract system (Phase B), `BuildSystem`

---

### Contract System

**What it is:** A data object bundling blueprint reference, time limit, allowed
materials, active modifiers, base payout, and partial-payout thresholds.

**Decisions made:**
- Contract is defined in data (JSON or ScriptableObject, TBD)
- `ContractDefaults` and `CompletionThresholds` already exist in `BlueprintData.cs`
  as structs — contracts may extend these or replace them
- Partial payout thresholds: 100% completion = full payout, lower thresholds give
  partial payout

**What to build:**
- `ContractData` class (or reuse/extend `ContractDefaults`)
- `ContractManager` that the Hub terminal (or contract selection screen) writes to
  and `BuildSystem` reads from
- Integration with win/loss evaluation

**Open questions:**
- Does a contract live inside a blueprint file, or is it separate data that
  references a blueprint?
- For MVP contract, can it just be hardcoded values?

---

### Win / Loss Conditions

**What it is:** Level ends when the blueprint reaches 100% completion OR the timer
expires. Outcome (success/failure) determines payout.

**Decisions made:**
- `BuildSystem` already tracks tile states — completion % is derivable from that
- `BuildSystem` fires an event when all required tiles are Built
- Timer expiry also triggers end

**What to build:**
- Completion percentage calculation in `BuildSystem`
- Level-end trigger (both paths: completion event and timer expiry)
- Payout calculation based on completion % and `CompletionThresholds`
- Scene transition back to contract selection (or Hub) after results

---

## Phase C — Economy

### Shared Money Pool

**What it is:** A single number representing the team's current money. Persists
across contracts within a run. Resets when a run ends.

**Decisions made:**
- Server-authoritative, synced via `NetworkVariable<int>`
- One pool shared by all players, not tracked per-player
- Stored in a run-level object (the future `GameSession` NetworkObject — note
  naming conflict with current non-networked `GameSession`, see `ARCHITECTURE.md`)

**Open questions:**
- What is the starting money amount?

---

### Company Fee

**What it is:** A fee deducted after each contract. Scales with round number.

**Decisions made:**
- Rounds 1-6: low linear growth scaled with round number
- Round 7+: higher linear rate (endless escalation)
- Run ends when money hits zero after fee deduction

**Open questions:**
- What are the specific fee values per round?

---

### Shop

**What it is:** Available from the contract selection screen between contracts.
Aesthetic intent: the Home Depot.

**Categories:**
- Tools — unlock new types or extra copies
- Speed upgrades — faster build interactions per material
- Utility — tarps, epoxy coating, wheelbarrow (carries two items)
- Chaos countermeasures — termite killer, structural reinforcements
- Wild cards — high-cost, high-impact items

Full item list is in the original GDD content reference (see `docs/GAME_INTENT.md`
Section 8).

**Open questions:**
- Does the shop show all items always (some locked/greyed), or only items the team
  can afford?
- How many items show per category?

---

## Phase D — Chaos Events

### Event Framework

**What it is:** A self-contained system where events can spawn, behave, and clean
up independently. Networked so every player sees the same event state.

**Build this first, before any individual event.** Validate the full pipeline with
one simple event (Termites) before building the full list.

**Decisions made:**
- Events are self-contained (own spawn, behavior, cleanup)
- Events respect the active contract's modifier list
- Events are networked — server-authoritative, clients render

**What to build:**
- `ChaosEvent` base class with Activate/Deactivate lifecycle
- `ChaosEventManager` (server-side) that selects and fires events on a schedule
- Event registration system so new events can be added without touching the manager

---

### Individual Events (Priority Order)

1. **Termites** — structural damage over time to wood tiles. Simple: pick a
   Built wood tile, tick down its health, revert to Placed or Empty if destroyed.
   Validates the event framework end-to-end.

2. **Rain** — disables concrete placement, damages exposed materials. Environmental.

3. **Zombies** — enemy NPCs that interfere with players and/or damage tiles.
   Validates the enemy event category.

Full event list (Oakland, Panzer, OSHA, Gilbert, Skibidi, Big Bad Wolf, etc.) is in
the original GDD. Implement after the framework is validated.

---

## Phase E — Content and Replayability

### Contract Selection Screen

**What it is:** Before each level, shows 2-3 contract options. Players vote or host
selects. Contracts vary in difficulty, modifier count, and payout.

**Decisions made:**
- 2-3 options shown
- Players vote OR host selects (TBD)
- Contracts vary in: difficulty, modifier count, payout

**Open questions:**
- How are contracts generated — handcrafted list, procedural parameters, or
  blueprint-driven?

---

### Contract Modifiers

| Modifier         | Effect |
|------------------|--------|
| Reinforcement    | Extra material needed on certain tiles |
| Rain             | Disables concrete, damages exposed materials |
| Rush             | Less time, more payout |

More modifiers map onto chaos events from the full GDD list.

---

## Phase F — Generation and Editor

### Level Editor (Scene Wiring)

The Level Editor scripts are fully written (`Assets/Scripts/LevelEditor/`). The
scene and GameObject wiring do not exist yet. This is a manual Unity Editor step.
See `docs/wiring/` for the pending wiring task.

**What still needs to happen:**
- Create `LevelEditor.unity` scene
- Place and configure all LevelEditor GameObjects per the wiring doc
- Connect Save to `StreamingAssets/Blueprints/` and Steam Cloud

---

### Modular Blueprint Assembly (Post-Editor)

Procedural combination of handcrafted room modules into larger blueprints. Design
TBD. No implementation started.

---

## Structural Integrity / Collapse Cascade

**What it is:** A `supportDependents` graph where destroying a load-bearing tile
triggers a collapse cascade (Jenga-style). Planned for Phase D alongside chaos
events since it's most useful as an event outcome.

**Decisions made:**
- Each tile tracks what it supports
- Destroying a tile evaluates whether dependents are still supported
- Unsupported tiles fall/are destroyed in sequence

**Dependencies:** Build tile dependency system (partially exists), chaos event
framework

---

## Death / Respawn / Ghost Mode

**What it is:** Players can be incapacitated. Ghost mode lets them observe and
potentially help in a limited way while waiting to respawn.

**Design TBD.** No implementation started. Not needed for MVP.

---

## Player-Count Scaling

**What it is:** Tunables (material cap, delivery cost, fee, time limit) that adjust
based on how many players are in the session.

**Decisions made:**
- This should exist — a 4-player game should have proportionally more materials
  and more time than a 2-player game
- No specific values decided yet

**Dependencies:** Contract system, OrderQueueSystem, LevelTimer

---

## Weight Classes and Speed Penalties

**What it is:** Movement speed modifier applied when carrying objects above Light
weight class.

**Decisions made:**
- Light: no penalty
- Medium: minor penalty (specific value TBD)
- Heavy: 75% speed reduction

**What to build:**
- Speed multiplier applied in `PlayerController` based on held object's weight class
- `PhysicsPickup` exposes `WeightClass` to `PlayerInteraction`
- `PlayerController` reads current weight and applies multiplier

**Dependencies:** `PlayerController`, `PlayerInteraction`, `PhysicsPickup`,
`MaterialItem`/`ToolItem`
