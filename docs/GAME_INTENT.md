# Crazy Construction — Game Intent

**By Cameron Lee, Kyle Wong**

This document describes *why* mechanics exist and what they are supposed to feel
like. It is the design intent layer. For what is actually built today, see
`docs/ARCHITECTURE.md`. This file is **read-only for Claude Code** unless Cameron
explicitly says to update it. If an implementation decision changes the intent or
purpose of a mechanic, flag it and ask before touching this file.

---

## 1. Concept

A first-person co-op party game for 1-4 players on PC (Steam). Players are given a
blueprint defining a structure to build. They gather raw materials, carry them to
build tiles, and use tools to place and refine them into the finished structure.
Chaos events, contract modifiers, and a rogue-lite economy create escalating
pressure across a run.

---

## 2. Core Pillars

These five pillars are the filter for every design decision. If something doesn't
serve at least one of them, it probably doesn't belong in the game.

**Blueprint-driven goals** — Players always have a clear target state. The structure
defines what needs to be done. There are no phase announcements, no NPC barking
orders, no artificial gates. The blueprint prop and the tile ghosts tell the whole
story.

**Implicit roles** — No assigned jobs. Tools and materials self-organize players
naturally. Whoever picks up the hammer becomes the wood person. This should emerge
from scarcity and tool availability, not from explicit assignment.

**Demand-driven construction** — Players can work on any part of the blueprint at
any time, provided structural dependencies are met. The structure itself enforces
build order tile by tile. This is the replacement for a sequential phase system.

**Chaos as pressure** — The core loop is intentionally simple. Chaos events are
what make each run feel different and create stories worth retelling. Events should
feel like they're happening *to* the players, not just adding difficulty.

**Rogue-lite replayability** — Contracts, a shop system, and an escalating economy
give each run stakes and variation. Losing a run should feel meaningful, not
arbitrary.

---

## 3. Core Gameplay Loop

```
START RUN
  → Contract Selection Screen
      → Shop (optional)
          → Load Level (blueprint + map)
              → Build Phase
                  Players gather, carry, place, refine materials
                  Chaos events fire throughout
                  Level ends: blueprint complete OR timer expires
                      SUCCESS: earn contract payout, deduct company fee
                      FAILURE: earn partial payout, deduct company fee
  → Contract Selection Screen (next round)
      → Run ends when money hits zero after fee
```

A run is a series of contracts. Each contract is one level. Money persists across
contracts within a run. All progress resets when the run ends.

---

## 4. Systems

### 4.1 Interaction

Pick up, carry, and drop one object at a time. Objects are throwable.

**Weight classes:**
- **Light** (wood plank, trowel) — no penalty
- **Medium** (concrete bag) — minor speed penalty
- **Heavy** (steel beam) — 75% speed reduction

Heavy objects support two-player shared carry: a second player binds in, movement
averages between both, and either can release independently.

### 4.2 Materials

| Material     | Weight | Tool          | Notes |
|--------------|--------|---------------|-------|
| Wood Plank   | Light  | Hammer        | Default starting material |
| Concrete     | Medium | Trowel        | Cement + water mixed first; 20s hardening timer that resets if stepped on |
| Steel Beam   | Heavy  | Welding Torch | Two-player carry recommended |

States: Raw → Held → Placed → Built.

Materials spawn at designated supply zones and respawn after a cooldown. Supply zone
types (wood-only, mixed, etc.) are set per contract.

### 4.3 Tools

| Tool          | Material | Build Duration | Notes |
|---------------|----------|----------------|-------|
| Hammer        | Wood     | 2.0s           | |
| Trowel        | Concrete | 2.5s           | |
| Welding Torch | Steel    | 4.0s           | Requires stillness during build |

A tool is held like any object. Approaching a build tile with the correct placed
material and correct tool shows a hold-to-build prompt and progress bar. Completing
it flips the tile from Placed to Built. Tools spawn at a tool depot area.

### 4.4 Build Tiles

Each tile has: grid position, required material, required tool, state
(Empty → Material Placed → Built), and structural dependencies.

**Dependency rules:**
- Foundation — no dependencies
- Floor — needs an adjacent Built foundation
- Wall — needs an adjacent Built floor
- Furniture — needs an adjacent Built wall
- Decor — needs an adjacent Built tile

Visual states: ghost/holographic outline (Empty), raw material visible
(Material Placed), finished piece (Built).

**Structural integrity** (planned, not built): a `supportDependents` graph and
Jenga-style collapse cascade when a load-bearing tile is destroyed.

### 4.5 Blueprints

The target state for a level: grid layout, per-tile requirements, completion
thresholds (100% for full payout, partial thresholds for partial payout), and a
physical blueprint prop in the world players can reference alongside tile ghosts.

Post-MVP: modular procedural blueprint assembly from handcrafted room modules.
MVP: one handcrafted blueprint is sufficient.

### 4.6 Contracts

A contract bundles: blueprint reference, allowed materials, time limit, active
modifiers, base payout, partial-payout thresholds.

**Modifiers:** Reinforcement (extra material on certain tiles), Rain (disables
concrete, damages exposed wood/steel), Rush (less time, more payout).

Before each level, a contract selection screen shows 2-3 options varying in
difficulty, modifier count, and payout. Players vote or the host selects.

### 4.7 Economy

Money is one shared pool. Earned by completing contracts. Spent in the shop between
contracts. Company fee deducted after each contract.

**Fee scaling:** Rounds 1-6 scale with round number (low linear growth). Round 7+
grows at a higher linear rate (endless escalation). Run ends when money hits zero
after the fee is deducted.

### 4.8 Shop

Available from the contract selection screen. The aesthetic intent: this is the
Home Depot.

Categories:
- **Tools** — unlock new types or extra copies
- **Speed upgrades** — faster build interactions per material
- **Utility** — tarps, epoxy coating, wheelbarrow (carries two items at once)
- **Chaos countermeasures** — termite killer, structural reinforcements
- **Wild cards** — high-cost, high-impact items

Full item list is in the original GDD content reference (Section 7 below).

### 4.9 Chaos Events

Events are self-contained (their own spawn, behavior, cleanup), networked so every
player sees and is affected by the same event state, and respect the active
contract's modifier list.

**Categories:**
- **Environmental** — weather, earthquakes, salted soil
- **Enemy** — zombies, aliens, looters, wolves
- **Structural** — termites, building collapse
- **Scripted** — named set-piece events (Oakland, Panzer, OSHA, Gilbert, Skibidi)

**Build priority:** empty event framework first, then validate the full pipeline
with one simple event (Termites), then expand the full list.

---

## 5. Multiplayer Intent

Server-authoritative for all gameplay-critical state: object ownership, build tile
state, money, event triggers. Client-predicted for movement and held-object
positions only. Build completion validated server-side only. Proximity voice
(Vivox) works naturally with first-person perspective — no extra design work needed.

---

## 6. MVP Definition

*"The smallest version of this game that can be played with a friend for 20 minutes
and feel like something."*

**Includes:** first-person player controller and interaction; one material (Wood,
light, no penalty); one tool (Hammer); build tile system with ghost outlines; one
handcrafted blueprint (~10-15 tiles); build progress bar and completion visual;
multiplayer (two players interacting with the same world); basic supply zone where
wood spawns.

**Excludes:** Concrete/Steel; economy/contracts; shop; chaos events; level editor;
modifiers; timer; money.

**Success criteria:** Two players load in, pick up wood planks from a supply zone,
carry them to build tiles, hammer them into walls, and see a structure visually take
shape. The loop feels satisfying to do once.

---

## 7. Post-MVP Development Order

- **Phase A** — Complete the material loop: Concrete (cement + water + hardening
  timer), Steel (heavy + two-player carry), remaining tools (Trowel, Welding Torch)
- **Phase B** — Game layer: timer, contract system (one handcrafted contract),
  win/loss conditions, basic payout
- **Phase C** — Economy: money system, company fee, shop with initial item set
- **Phase D** — Chaos: event framework, 2-3 events (Termites, Rain, Zombies),
  then expand
- **Phase E** — Content and replayability: contract selection screen, modifiers,
  full shop item list, more blueprints
- **Phase F** — Generation and editor: level editor (dev tool first, player unlock
  second), modular blueprint assembly

---

## 8. Original Content Reference

The chaos events, shop items, and story/setting from the original GDD remain valid
content targets. That document contains the full event list (Oakland, Panzer, OSHA,
Gilbert, Big Bad Wolf, etc.) and the full shop item table. This content slots into
the chaos event and shop systems defined above without requiring architectural
changes.

---

## 9. Credits

- Lead Game Designer/Developer: Cameron Lee
- Game Designer: Kyle Wong
- Asset Artist: Gilbert Zavala
