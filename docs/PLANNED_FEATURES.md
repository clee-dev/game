# Planned Features

Everything in this document is designed but not yet implemented. Organized by
development phase from `docs/GAME_INTENT.md`. Each entry includes what the feature
is, key decisions already made, dependencies, and open questions.

This is the product backlog. When starting work on any feature here, move it to
`docs/ARCHITECTURE.md` once it's built.

---

## Hub Systems

### Hub Terminal (Blueprint Selector) — BUILT, see `docs/ARCHITECTURE.md`

Implemented as `HubTerminal` (`Assets/Scripts/Build/HubTerminal.cs`), placed in
`Hub.unity` alongside (not replacing) `LevelSelectKiosk`. Full detail in
`docs/ARCHITECTURE.md`'s "Hub Terminal" section. Summary of how the open questions
above were resolved, asked directly to Cameron:
- **Host selects vs. vote:** no host gate — any connected player's confirm writes
  the synced selection, same any-player-confirms behavior `LevelSelectKiosk` already
  had. No vote system.
- **Browse vs. confirm:** anyone can browse/highlight anytime, independent of
  confirmation; only confirming (Enter) is networked.
- **Preview:** yes, a simple top-down tile-color preview (procedural `Texture2D`,
  cropped to content, cached per blueprint).

**Built-in deviation from this doc:** rendered via screen-space `OnGUI()`, not a
world-space Canvas — this codebase has no EventSystem/PhysicsRaycaster anywhere for
world-space UI raycasting, and every other in-game menu is already `OnGUI()`-based.
Flagged in `docs/ARCHITECTURE.md` and `docs/SESSION.md` for Cameron to confirm this
adaptation is acceptable.

`LevelSelectKiosk` is unchanged and still in the Hub as the documented fallback.

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

### Steel Material — Built (code), prefab/blueprint pending

**What it is:** A heavy buildable material requiring two-player carry.

**Decisions made:**
- Weight class: Heavy — 75% movement speed reduction when held solo
- Two-player shared carry via two interchangeable attach points (no fixed
  primary/secondary point — either player can grab either one); movement
  averages between both, either can release independently
- If the owner releases while shared, the other holder becomes the new owner
  (ownership handoff, not a drop)
- Carry orientation follows the line between the two carriers' body
  positions, not either carrier's facing — looking around while stationary
  doesn't spin the carried item
- Drifting more than `maxShareDistance` apart while shared auto-releases the
  non-owner side
- Tool: Welding Torch (requires stillness during build interaction)
- Build duration: 4.0s (slower than Hammer/Trowel)
- Torch has a burnout meter, not unlimited use (resolves the fuel/durability
  open question below) — see Remaining Tools

**Built:**
- `WeightClass` enum (`Assets/Scripts/Blueprint/BlueprintEnums.cs`) — shared by
  any future Medium/Heavy material, not Steel-specific.
- `PhysicsPickup.Weight` exposes it per-prefab; `PlayerController.CurrentWeightMultiplier()`
  applies it to walk/sprint speed (see Weight Classes and Speed Penalties below).
- `TwoPersonCarry` (`Assets/Scripts/TwoPersonCarry.cs`) — symmetric two-point
  redesign (latest session): two `NetworkVariable<ulong>` holder slots
  (`_holderA`/`_holderB`, one per `TwoPersonCarryPoint.pointIndex`, no fixed
  primary/secondary), `RequestBindRpc(clientId, pointIndex)`/`RequestUnbindRpc`,
  `TryHandoffOnOwnerRelease(ulong)` (called from
  `PhysicsPickup.RequestDropServerRpc` when the owner releases while shared),
  a server-only `Update()` auto-releasing the non-owner side past
  `maxShareDistance`. `CarryPointFor(Transform)` derives each carrier's carry
  point from their body root (not camera/holdPoint) — see
  `docs/ARCHITECTURE.md` Interaction & Pickup for why.
- `TwoPersonCarryPoint` (`Assets/Scripts/TwoPersonCarryPoint.cs`) — marker on
  one of two interchangeable attach-point colliders (`pointIndex` 0/1),
  resolved by `PlayerInteraction`'s existing raycast.
- `PlayerInteraction.HandleCarryBinding`/`MoveHeldObject` shared-carry
  averaging (rotation now from carrier body positions, not facing — fixes
  rotation jank while a stationary carrier looks around),
  `ReconcileCarryHandoff` (promotes a bound carrier to full holder after an
  ownership handoff), `[E] Pick Up` / `[E] Help Carry` / `[E] Let Go (Carrying)`
  prompts. Generic body-collider pickup (`TryPickup`) refuses any item with a
  `TwoPersonCarry` component — must go through an attach point.
- `WeldingTorchFuel` burnout meter (see Remaining Tools) and `BuildTile`'s
  stillness-pause integration.

**Still to build (Cameron, in-Editor — see
`docs/wiring/steel-material-and-welding-torch.md` and
`docs/wiring/steel-two-person-carry-points.md`):**
- `Steel.prefab` needs a *second* `TwoPersonCarryPoint` child (it currently
  has one, built before the symmetric redesign) plus `pointIndex` set on both
- `WeldingTorch` prefab
- Blueprint content: a Steel tile, Steel supply zone, Welding Torch depot

**Dependencies:** `MaterialItem`, `PhysicsPickup`, `PlayerInteraction`, `ToolItem`,
`ToolStats` — all satisfied.

**Open questions — resolved:**
- How does the second player "bind in"? → dedicated attach-point collider
  (`TwoPersonCarryPoint`), same raycast+interact-prompt pattern as every other
  interactable, not proximity-based.
- Is the stillness threshold velocity or full-stop? → full-stop on movement
  *input* (`InputReader.MoveInput.sqrMagnitude < 0.0001f`), not a
  `CharacterController` velocity reading — an implementation-detail
  simplification, flagged for Cameron to revisit if it doesn't feel right.
- Does the torch have fuel/durability? → yes, a burnout meter (heats while
  welding, locks out once full, drains while idle) — not unlimited, not
  consumable/permanent-durability either. See Remaining Tools for tunables.

---

### Remaining Tools (Trowel, Welding Torch)

**Trowel:** Medium-weight tool, 2.5s build duration on Concrete tiles. Not built —
no prefab, no gameplay.

**Welding Torch — gameplay code built, prefab pending.** Light-weight tool (the
player carrying it is slowed — the steel beam they're also carrying is what's
heavy), 4.0s build duration, requires stillness (pause, not cancel — see Steel
Material above). Also has a burnout meter (`WeldingTorchFuel`,
`Assets/Scripts/Build/WeldingTorchFuel.cs`): `maxHeat` 8s (placeholder) of
continuous welding before it overheats and locks out for `cooldownDuration` 4s
(placeholder); drains at `drainRate` 1/sec any time it isn't actively welding,
not just during the cooldown, so short bursts recover between builds.
`BuildTile.ContinueBuildRpc`/`BuildTickCoroutine` calls `TryHeat()` once per
build tick and withholds progress (without resetting it) for any tick it
returns `false`. Needs: prefab, `ToolItem` component (code already supports
`ToolType.Torch`), depot spawner entry.

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

## Weight Classes and Speed Penalties — Built

**What it is:** Movement speed modifier applied when carrying objects above Light
weight class.

**Decisions made:**
- Light: no penalty
- Medium: minor penalty — implemented as a placeholder `0.85` multiplier
  (`PlayerController.mediumWeightMultiplier`, `[Range(0,1)]`, Cameron to tune —
  no material uses Medium yet, so there's nothing to playtest the feel against)
- Heavy: 75% speed reduction — `heavyWeightMultiplier = 0.25f`
- A shared two-player carry (`TwoPersonCarry.IsShared`) drops the penalty to
  1.0 for both carriers regardless of weight class — "no penalty once shared"

**Built:**
- `WeightClass` enum (`Assets/Scripts/Blueprint/BlueprintEnums.cs`)
- `PhysicsPickup.weightClass` `[SerializeField]` + `Weight` property
  (`Assets/Scripts/PhysicsPickup.cs`)
- `PlayerController.CurrentWeightMultiplier()` (`Assets/Scripts/PlayerController.cs`)
  reads `PlayerInteraction.HeldObject`, checks for a shared `TwoPersonCarry`
  first, then maps `WeightClass` to a multiplier; applied to both walk and
  sprint speed in `Move()`.

**Wiring needed:** `PlayerController.playerInteraction` is unassigned on
`Player.prefab` (both components already live on the prefab root) — see
`docs/wiring/steel-material-and-welding-torch.md`. No material currently uses
`Medium` (Concrete, the only planned Medium material, isn't built yet), so this
system is exercised today only by Heavy (Steel, once its prefab exists).

**Dependencies:** `PlayerController`, `PlayerInteraction`, `PhysicsPickup`,
`MaterialItem`/`ToolItem` — all satisfied.
