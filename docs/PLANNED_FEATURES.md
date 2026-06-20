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

### Timer System — Built

**What it is:** A countdown per level set by the active contract. When it hits zero,
the level ends with whatever completion percentage was reached.

**Decisions made:**
- Timer is server-authoritative, synced to all clients via `NetworkVariable<float>`
- Visible to all players (world-space HUD or screen UI)
- When timer expires, trigger same completion evaluation as manual completion
- Did not wait on the full Contract System below — `BlueprintData.contractDefaults`
  (`timeLimitSeconds`, `completionThresholds`) already existed and is already
  populated in every blueprint JSON, so `LevelTimer` reads it directly. The
  Contract System's `ContractData`/`ContractManager` layer is still open (see
  below) for anything beyond a single hardcoded-per-blueprint contract.

**Built:**
- `LevelTimer` (`Assets/Scripts/Build/LevelTimer.cs`) — scene-placed
  `NetworkBehaviour`, seeds `NetworkVariable<float>` from
  `BuildSystem.CurrentBlueprint.contractDefaults.timeLimitSeconds` on spawn,
  ticks down server-side, fires `BuildSystem.EvaluateCompletion(forced: true)`
  via its own `OnValueChanged` (so every machine, not just the server, reacts
  identically) when it crosses zero.
- `BuildSystem.EvaluateCompletion(bool forced)` — shared by natural completion
  (`BuildTile.OnStateChanged`, all tiles Built) and forced (timer expiry).
  Guards against firing twice, compares `CompletionPercent` against
  `contractDefaults.completionThresholds.full`, fires `GameEvents.OnLevelEnded`.
- `GameEvents.OnLevelEnded` — new event, decouples consumers from `BuildSystem`.
- `LevelTimerHUD` (`Assets/Scripts/Build/LevelTimerHUD.cs`) — MM:SS display,
  swaps to "Complete!" / "Time's Up — N%" on level end.
- Scene wiring in `Game1.unity` (hand-edited YAML, **unverified in-Editor** —
  see `docs/SESSION.md`).

**Explicitly not built (out of scope for this pass):** payout calculation,
post-level scene transition — see Win/Loss Conditions below.

**Dependencies:** `BuildSystem` (satisfied). Contract system below is no longer
a hard dependency for the MVP timer.

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

### Win / Loss Conditions — partially built

**What it is:** Level ends when the blueprint reaches 100% completion OR the timer
expires. Outcome (success/failure) determines payout.

**Decisions made:**
- `BuildSystem` already tracks tile states — completion % is derivable from that
- `BuildSystem` fires an event when all required tiles are Built
- Timer expiry also triggers end

**Built (as part of Timer System above):**
- Completion percentage calculation in `BuildSystem` (`CompletionPercent`,
  pre-existing)
- Level-end trigger, both paths: `BuildTile.OnStateChanged` (natural, all tiles
  Built) and `LevelTimer` expiry (forced) both route through
  `BuildSystem.EvaluateCompletion`, which fires `GameEvents.OnLevelEnded(bool
  success, float completionPercent)`
- Scene transition back to the Hub — manual, via the Level Summary UI's
  "Return to Hub" button (see Level Summary below), not automatic

**Still to build:**
- Payout calculation based on completion % and `CompletionThresholds` (a
  listener on `GameEvents.OnLevelEnded`, not yet written)

---

### Level Summary — partially built

**What it is:** Screen shown when a level ends (`GameEvents.OnLevelEnded`),
reporting the outcome and eventually a per-player "blame summary" (who made
the most mistakes, who carried the most/least, death count, etc.), with a
button to return to the Hub.

**Built:**
- `LevelSummaryUI` (`Assets/Scripts/Build/LevelSummaryUI.cs`) — listens for
  `GameEvents.OnLevelEnded`, shows `resultText` ("Level Complete" / "Time's
  Up") and `completionText` (`N% Built`), unlocks the cursor
- `Return to Hub` button — relays through the local player's
  `NetworkPlayer.RequestLoadSceneRpc("Hub")`, same mechanism `PauseMenu`'s
  "Leave to Hub" uses
- Scene wiring in `Game1.unity`: `SummaryCanvas` → `SummaryPanel` →
  `ResultText` / `CompletionText` / `ReturnToHubButton`, hand-edited YAML,
  **unverified in-Editor** — see `docs/SESSION.md`

**Still to build — blame summary (explicitly deferred, not started):**
- Per-player stat tracking during the level: mistakes (wrong material placed?
  wasted material? TBD what counts as a "mistake"), deaths, materials
  carried/delivered, builds completed, etc. — none of this is tracked
  anywhere yet, this needs new systems, not just UI
- The actual blame summary display (leaderboard-style breakdown per player)
- **Reserved slot:** `LevelSummaryUI.blameSummaryRoot` — an inactive, empty
  `RectTransform` (`BlameSummaryRoot`, inside `SummaryPanel` in `Game1.unity`)
  is where this goes once built. Don't repurpose this GameObject for anything
  else.

**Open questions:**
- What exactly counts as a "mistake" for blame purposes? (wrong material on a
  tile, wasted/dropped materials, time spent idle, etc. — needs Cameron's call)
- Where does per-player stat tracking live — on `NetworkPlayer` itself, or a
  separate stats-tracking NetworkBehaviour?
- Does the blame summary need to be visible to all players at once (a single
  synced ranking) or can each client compute it locally from already-synced
  state?

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

### Level Editor (Build Settings / Player Access)

`LevelEditor.unity` is built and fully wired (scripts + scene + GameObjects).
Saves already write to `StreamingAssets/Blueprints/` and Steam Cloud, so
anything saved there is immediately selectable from the Hub kiosk. Not yet
done: adding the scene to `EditorBuildSettings` for in-game/player access.
Per `GAME_INTENT.md` Phase F ("dev tool first, player unlock second"), this is
intentionally deferred, not a gap to fix now.

---

### Modular Blueprint Assembly (Post-Editor)

Procedural combination of handcrafted room modules into larger blueprints. Design
TBD. No implementation started.

---

## Structural Integrity / Collapse Cascade — built

**What it is:** Destroying a load-bearing tile triggers a collapse cascade
(Jenga-style) through anything that depended on it for support. Originally
planned for Phase D alongside chaos events since it's most useful as an event
outcome — built ahead of schedule as infrastructure, since chaos events
(the eventual real trigger) don't exist yet.

**Decisions made (revised from the original plan above):**
- No separate `supportDependents` graph. `TileStructuralRules.HasSupport`
  already re-derives "is this position currently supported" live from
  neighbor tile states (used by both `BuildSystem` at runtime and the Level
  Editor at author-time) — a cached reverse-edge graph would just be a second
  copy of the same information with its own staleness risk. The cascade
  reuses it via `BuildSystem.IsEligible` instead.
- Destroying a tile (`BuildTile.Collapse()`) checks its up + 4 horizontal
  neighbors (`BuildSystem.CascadeCollapseFrom`) and collapses any that are no
  longer supported by anything else. Each collapse re-enters `OnStateChanged`,
  so the cascade continues automatically with no explicit recursion
  bookkeeping. Terminates because a tile can only transition to `Destroyed`
  once (idempotent guard in `Collapse()`).
- `Destroyed` is **repairable** — treated like `Empty` for
  `CanAcceptMaterial`, still gated by `IsEligible` so a tile can't be
  rebuilt until whatever supports it is restored first. Chosen over
  permanent loss to match the "demand-driven construction" pillar and avoid
  unwinnable states.
- No in-game trigger exists yet (chaos events are still Phase D, not built).
  Added a **host-only debug trigger** (Backspace, via `PlayerInteraction`)
  so the cascade can be tested now instead of waiting on the chaos event
  framework — explicitly a stand-in, not the intended real trigger.

**Built:**
- `BuildTile.Collapse()` — stops any in-progress build, despawns a placed
  raw material via `MaterialItem.DestroyInCollapse()`, resets build
  progress, sets state to `Destroyed`.
- `BuildSystem.CascadeCollapseFrom(Vector3Int)` / `CollapseIfUnsupported` —
  server-only, checks up + 4 horizontal neighbors against `IsEligible`.
- `MaterialItem.DestroyInCollapse()` — despawns the raw material sitting on
  a collapsing `MaterialPlaced` tile.
- Red-tinted ghost visual on `Destroyed` tiles (`BuildTile.RefreshVisual`).
- Debug-only demolish trigger + on-screen hint in `PlayerInteraction.cs`,
  gated by `IsServer` (host-only, no RPC needed).
- See `docs/ARCHITECTURE.md`'s "Structural Integrity / Collapse Cascade"
  section for full detail.

**Still to build:** the real trigger (chaos events, Phase D) to replace the
debug key — the debug trigger should be removed once that lands.

**Unverified:** no Unity Editor available in this environment this session —
see `docs/SESSION.md` for the playtest checklist.

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
