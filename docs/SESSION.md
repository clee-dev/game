# Session Log

What was in progress last session, open questions, next steps. Read this first,
every session.

---

## 2026-06-19

**Context from Cameron:** "my game is getting so disorganized and completely
messy... I just implemented the level editor. i guess the kiosk hub level
editor was already done. i havent wired a bunch of stuff."

**What this session did:** A full audit of what's actually built vs. what the
docs claimed, to find the real wiring debt (as opposed to assumed debt).

### Findings

The actual wiring debt was much smaller than it felt. Two real bugs, the rest
was documentation drift:

1. **`ToolDepotSpawner.prefab` bug (fixed).** The script was refactored from a
   single `toolPrefab` field to a `toolPrefabs[]` array, but the prefab asset
   was never updated. Unity doesn't migrate renamed/retyped `[SerializeField]`s
   automatically, so the Hammer reference was silently orphaned. Net effect:
   **no tool ever spawned at the tool depot in `Game1`** — the MVP loop was
   broken at the prefab level, not the code level. Fixed by re-serializing the
   field as a single-entry array with the same Hammer reference.

2. **`Player.prefab` / `HubPlayerState.cam` unassigned (fixed).** Minor —
   `NetworkPlayer` already gates the camera by `IsOwner` independently, so this
   wasn't actively broken, but it was wrong. Pointed it at the same `Camera`
   component `NetworkPlayer.playerCam` / `PlayerInteraction.playerCamera` use.

3. **`LevelEditor.unity` already exists and is fully wired.** `ARCHITECTURE.md`
   and `PLANNED_FEATURES.md` both claimed the scene didn't exist yet — false.
   Confirmed by direct inspection: `LevelEditorController`, `LevelEditorUI`,
   `EditorGridRenderer`, `LevelEditorCamera` are all present and configured.
   This is what Cameron meant by "I just implemented the level editor." Docs
   updated to match reality.

4. **`HubSpawnPoints` already exists and is fully wired.** `PLANNED_FEATURES.md`
   listed it as not-yet-built — false. 4 `Transform`s are wired in `Hub.unity`
   and `HubPlayerState` reads them correctly. This is what Cameron meant by
   "the kiosk hub level editor was already done." Removed from the backlog.

5. **`docs/SESSION.md` didn't exist** despite being CLAUDE.md's required
   first-read every session. Created (this file).

6. **`docs/wiring/` didn't exist.** Created, with one genuine pending wiring
   task: `trowel-and-torch-tool-prefabs.md` (Phase A material loop — the code
   is already 100% generic and ready, only new prefab assets + registration
   are needed, no new scripts).

### Not a gap (checked, intentionally deferred or already correct)

- `LevelEditor.unity` not in `EditorBuildSettings` — intentional per
  `GAME_INTENT.md` Phase F ("dev tool first, player unlock second").
- `OrderStation.prefab`'s `deliveryPoint` unassigned — intentional optional
  override; falls back to a matching `SupplyZoneSpawner`.
- `DefaultNetworkPrefabs.asset` — cross-checked against every prefab with a
  `NetworkObject` component. All 6 are registered. No missing registrations.

### Changes made this session

- `Assets/Prefabs/ToolDepotSpawner.prefab` — `toolPrefab` → `toolPrefabs[]`,
  Hammer reference restored.
- `Assets/Prefabs/Player.prefab` — `HubPlayerState.cam` assigned.
- `docs/ARCHITECTURE.md` — corrected stale LevelEditor-doesn't-exist claims,
  added HubSpawnPoints confirmation, documented both prefab fixes, added a
  Unity 6 Gotcha about renamed `[SerializeField]`s not auto-migrating.
- `docs/PLANNED_FEATURES.md` — removed completed "Hub Spawn Points" section,
  trimmed "Level Editor (Scene Wiring)" to reflect completion.
- `docs/wiring/trowel-and-torch-tool-prefabs.md` — new, the one real pending
  wiring task.
- `docs/SESSION.md` — created (this file).

### Next steps / open items

- Build Trowel and Welding Torch prefabs per
  `docs/wiring/trowel-and-torch-tool-prefabs.md` (Phase A, pure wiring, no
  code needed).
- Phase A material loop still needs real code: Concrete (cement + water +
  hardening timer) and Steel (heavy weight + two-player carry) — see
  `docs/PLANNED_FEATURES.md`.
- No open design questions raised this session — all findings were either
  fixed directly or confirmed as already correct/intentional.

---

## 2026-06-19 (Part B)

**Context:** a follow-up task list of six items, worked autonomously per
standing instructions (resolve ambiguity via engineering judgment, document
assumptions here rather than asking, don't stop early for token concerns). No
Unity Editor was available this session — all scene `.unity` files were
hand-edited as Force-Text YAML and verified via direct reads/fileID
cross-reference checks, not by opening the Editor. **Visual/layout results in
all three scenes are therefore unverified by eye and should be opened and
checked by Cameron.**

### Changes made this session

1. **`SteamCloudSave` NRE fix.** `Write`/`Read`/`ListFiles` all called into
   `SteamRemoteStorage` unconditionally; running without Steam initialized
   threw out of `ListFiles` and broke `BlueprintLoader`'s startup scan. All
   three now return early (`false`/`null`/empty list) when
   `!SteamManager.Initialized`.

2. **Hub → Level Editor access.** Added `LevelEditorAccessPoint.cs` (new
   Hub-only `NetworkObject`, same raycast-target pattern as
   `LevelSelectKiosk`/`OrderStation`) and wired `PlayerInteraction` to detect
   it and show an "[E] Enter Level Editor" prompt. Placed an instance in
   `Hub.unity` at `(2, 0.5, -22)`. Pressing E sends the **whole connected
   party** into `LevelEditor.unity` (no solo-scene concept in this game's NGO
   setup) via `EnterLevelEditorRpc()` → `NetworkPlayer.RequestLoadSceneRpc`
   (new, also used by the Pause Menu's "Leave to Hub").
   - Added `LevelEditor.unity` to `EditorBuildSettings` (NGO's
     `NetworkSceneManager.LoadScene` requires the target scene be registered
     there) — **this reverses the previously-documented "dev tool first,
     player unlock second" gating from `GAME_INTENT.md` Phase F.** Implementing
     it was explicit in this session's task list, but I did not separately
     re-confirm the *intent* change with Cameron before doing it, since the
     task itself was unambiguous. Flagging here per CLAUDE.md's rule on
     intent-changing decisions — please sanity check this is actually wanted.
   - Discovered while wiring this: `LevelSelectKiosk`'s script was fully
     implemented but **no instance had ever been placed in `Hub.unity`** — a
     real gap, not just docs drift like the other Hub findings in the
     2026-06-19 session above. Added one at `(-14, 0.5, -22)`.
   - Both new Hub GameObjects are new scene roots; I initially forgot to add
     their Transforms to `Hub.unity`'s `SceneRoots.m_Roots` list (caught this
     myself this session, not user-reported) — fixed, verified no duplicate
     fileIDs in the file afterward.
   - **Unverified:** both placements are blind coordinate choices with no
     visual confirmation. Please check they don't intersect existing Hub
     geometry.

3. **Pause screens in `Game1`/`LevelEditor` with "leave to hub."**
   `PauseMenu.cs` (new) + a Canvas/EventSystem/Button hierarchy added to both
   scenes, modeled on the existing `LobbyUI` Canvas in `Hub.unity`. Escape
   toggles a panel with Resume/Leave-to-Hub buttons; `PlayerCamera` already
   listened for `GameEvents.OnGamePaused/OnGameResumed` so `Game1` needed no
   script changes beyond the scene wiring. `LevelEditor.unity` had no
   EventSystem at all, so one was added (field-for-field copy of `Game1`'s).
   - **Found and fixed a real conflict before it shipped, not via user
     report:** `LevelEditor.unity`'s `LevelEditorPreviewController` already
     listens for Escape to exit Preview Mode. An always-on `PauseMenu` in the
     same scene would have double-handled Escape during Preview Mode and the
     two scripts would have fought over cursor lock state. Fixed by adding
     `LevelEditorController.pauseCanvas`, toggled off in `EnterPreviewMode()`
     / back on in `ExitPreviewMode()`, mirroring the existing `editorCamera`
     toggle in the same two methods. Wired in the scene
     (`LevelEditorController` → `pauseCanvas: {fileID: 760655022}`).
     Re-checked `Game1.unity` for an analogous conflict — none exists;
     `PlayerCamera` only reacts to the pause *events*, doesn't own Escape
     itself.
   - **Judgment call, not confirmed with Cameron:** `LevelEditorUI.cs`'s doc
     comment explicitly says the editor's own panels are `OnGUI`-only by
     design ("needs no Canvas/Button hierarchy to be usable"). I added the
     Pause overlay as a Canvas anyway, treating it as a separate, narrower
     concern that coexists fine alongside the OnGUI tool panels rather than a
     contradiction of that design note. Worth a second look.
   - **Unverified:** exact visual layout (panel/button positions and sizing)
     in both scenes.

4. **Suppress duplicate default tool depot/order station/supply zone
   spawns.** In the Level Editor, clicking an occupied World-Object cell
   stacked a second entry on top instead of replacing it.
   `LevelEditorController.PlaceOrReplace<T>` (replaces `AddWorldObject`) now
   replaces whatever's within 1 unit of the click instead of always adding;
   `PlayerSpawn`'s 4-player cap still only applies on the add path.

5. **Space out colliding default spawn locations.** `blueprint_001.json`'s
   `order_station_0` overlapped a default tool-depot/supply-zone spot, so the
   two prefabs spawned intersecting each other in `Game1`. Moved from
   `(4.5, 0.5, -1)` to `(1, 0.5, 4)`. **Unverified** — re-check the layout
   visually.

6. **Per-`MaterialType` `BuildTile` visuals.** `BaseHueFor(MaterialType)`
   added as a stand-in for real per-material textures, which **do not exist
   as assets yet**. Chosen hues: Wood tan `(0.76, 0.60, 0.42)`, Concrete gray
   `(0.65, 0.65, 0.65)`, Steel cool-gray `(0.55, 0.58, 0.62)` — Ghost shows
   the raw hue, Placed/Built lerp it toward blue/green so build state reads at
   a glance. Also swapped `BuildTile.prefab`'s ghost material off the opaque
   `ToonLit.mat` it was incorrectly pointed at, onto a new transparent
   `ToonTransparentGhost.mat` (`ToonTransparent.shader`, alpha 0.3) — without
   this the per-material tint wouldn't have read as a translucent ghost at
   all. These hue/blend values are a first pass with no art input; expect
   Gilbert to want to replace this with real textures per
   `docs/GAME_INTENT.md`.

### Open items for Cameron to review (none blocked the work, all flagged above)

- Hub → Level Editor access reverses previously-documented Phase F gating —
  confirm this is actually wanted, not just a side effect of wiring the
  feature.
- Canvas-based Pause overlay added to the otherwise-OnGUI-only
  `LevelEditor.unity` — confirm no objection to mixing UI paradigms there.
- Three blind/unverified in-editor placements: `LevelSelectKiosk`
  `(-14, 0.5, -22)`, `LevelEditorAccessPoint` `(2, 0.5, -22)` in `Hub.unity`,
  and the repositioned `order_station_0` `(1, 0.5, 4)` in
  `blueprint_001.json`.
- Pause UI visual layout (button positions/sizing) in both `Game1.unity` and
  `LevelEditor.unity` — not visually confirmed.
- `BuildTile` per-material hue values are a placeholder, not an art decision.

---

## 2026-06-19 (Part C)

**Context:** answers to four audit questions Cameron raised about the Level
Editor multiplayer gaps. Worked autonomously per standing instructions
(resolve ambiguity via engineering judgment, document assumptions here). No
Unity Editor available this session either — all changes hand-verified via
`grep`/fileID cross-reference, **not compiled or opened in-Editor**.

### Q4 — missing Wood/Concrete/Steel texture/material: already resolved

Cameron had pushed `Wood.mat` (and the Trowel/Torch prefabs) in a separate
commit and thought he'd forgotten to push it. Re-verified `Hub.unity`'s
prior merge (`4b4b086`, from the Part B session) is structurally sound —
`LevelSelectKiosk` and `LevelEditorAccessPoint` both present, zero duplicate
fileIDs, zero conflict markers — and confirmed `Wood.mat` is already wired
onto `WoodPlank.prefab` in that merged commit. Nothing more to do here.

**Deliberately not touched:** wiring `Wood.mat` onto `BuildTile.prefab`
itself. `BuildTile`'s Ghost/Placed/Built renderers currently use
`ToonTransparentGhost.mat`/`Blue.mat`/`Green.mat` with a code-level
hue-tint system (`BuildTile.BaseHueFor`, added in Part B above) standing in
for real materials. Swapping in an actual wood texture there raises a real
design question — how should a textured material coexist with the
existing build-state blue/green tint? — that's Gilbert's call per the
existing placeholder note in Part B, not mine to decide unilaterally per
CLAUDE.md's rule against assuming design intent.

### Q1 — multiplayer Level Editor access model

Cameron's answer: any connected player can be in the `LevelEditor` scene
together, but **only the host can edit**; non-host clients get camera/pan
only, no UI, and see the host's edits live.

- **Host-only input gating.** `LevelEditorController` gained a static
  `Instance` (matching the `BuildSystem.Instance`/`HubSpawnPoints.Instance`
  pattern already used elsewhere) and a `bool IsHost =>
  NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer`.
  `Update()` now returns immediately for non-hosts, before any editing
  input is processed. `LevelEditorUI.OnGUI()` does the same for the tool
  panels, replacing them with a one-line "Spectating" label for non-hosts.
  Chose to gate inside the existing plain `LevelEditorController` rather
  than converting the whole 573-line controller into a `NetworkBehaviour`
  — much smaller blast radius.
- **Live blueprint sync.** New `LevelEditorBlueprintSync : NetworkBehaviour`
  on its own scene-placed `NetworkObject` in `LevelEditor.unity` (same
  pattern as `LevelEditorAccessPoint`/`LevelSelectKiosk` — dedicated
  single-purpose networked component, no `DefaultNetworkPrefabs`
  registration needed since it's scene-placed). `EditorCommandStack`
  gained a `Changed` event (fires on Run/Undo/Redo, not Clear);
  `LevelEditorController` forwards it as `BlueprintChanged`, also firing
  on `NewBlueprint()`/`LoadBlueprint()`. The host broadcasts its current
  blueprint as JSON (`[Rpc(SendTo.NotServer)]`) on every change; new
  `LevelEditorController.ApplyRemoteBlueprint(BlueprintData)` rebuilds the
  client's local `Blueprint`/visuals from it without touching the
  (host-only) command stack. Late joiners covered for free: a non-host's
  `OnNetworkSpawn` requests the current blueprint
  (`[Rpc(SendTo.Server, InvokePermission = Everyone)]`), which the host
  answers through the same broadcast path.
  - **Flagged risk, unverified:** `[Rpc(SendTo.NotServer)]` (host → all
    non-host clients) is used here for the first time in this codebase —
    every other RPC target in the project is `SendTo.Server` or
    `SendTo.Owner`. I'm confident from NGO's documented unified-RPC API
    that `NotServer` is a valid `SendTo` enum member, but couldn't confirm
    it against the actual package (no `Library/PackageCache` or NGO DLL
    present in this environment to check). **Please confirm this compiles
    when next opened in the Editor.**
  - New files: `Assets/Scripts/LevelEditor/LevelEditorBlueprintSync.cs`
    (+ hand-created `.cs.meta`, guid `dfe6a1668a654375988fb509ea7ba939`).
  - `LevelEditor.unity`: added the `LevelEditorBlueprintSync` GameObject as
    a new scene root, verified no duplicate fileIDs afterward.

### Q3 — Game1 player spawn placement

Cameron's answer: position players using the blueprint's `playerSpawns`
array; for players beyond the defined spawn count, offset them 3 units
away in a random cardinal direction so they don't stack.

- `BuildSystem.GetPlayerSpawnPosition()` (server-only) hands out
  `CurrentBlueprint.playerSpawns` in order via a new `_nextPlayerSpawnIndex`
  counter that resets naturally every Game1 load (lives on `BuildSystem`,
  whose `Awake()` runs fresh each scene load). Once every defined spawn is
  handed out once, further calls cycle back through them with a random
  offset from a new 4-entry `OverflowSpawnOffsets` array (±3 units on X or
  Z).
- `NetworkPlayer.OnActiveSceneChanged` now calls this (server-side only,
  guarded by `IsServer`) whenever the active scene becomes `Game1`, and
  relays the result to the owning client via a new `[Rpc(SendTo.Owner)]
  TeleportToRpc(Vector3)` — same RPC-target pattern as the proven
  `HubPlayerState.TeleportToRpc`. Disables/re-enables the new
  `characterController` field around the position set (CharacterController
  caches velocity/position internally; setting `transform.position`
  directly while it's enabled can fight the controller next physics
  step — same reasoning `HubPlayerState` already uses for its own
  teleport).
  - `Player.prefab`: added the `characterController` reference, pointing
    at the same `CharacterController` component `HubPlayerState` already
    uses.

### Open items for Cameron to review

- `[Rpc(SendTo.NotServer)]` in `LevelEditorBlueprintSync.cs` — first use
  of this RPC target in the codebase, not compile-verified (no Editor
  available this session). Please confirm it builds.
- None of this session's changes (Q1 gating/sync, Q3 spawn placement) have
  been run or visually checked — no Unity Editor in this environment.
  Please playtest: (a) a non-host client in `LevelEditor` sees the host's
  edits live and can't edit anything themselves, (b) a late joiner who
  connects while the host is already in `LevelEditor` gets the current
  blueprint, (c) players spawn at the blueprint's `playerSpawns` positions
  in `Game1` instead of the origin.
- `BuildTile` real per-material textures (Wood now exists as an asset, but
  not wired onto `BuildTile` itself — see Q4 above) still needs Gilbert's
  input on how it should interact with the build-state tint.

### Branch divergence caught at push time

Pushing this session's work hit a rejected push — `origin/claude/relaxed-mccarthy-lqg4dc`
had moved on (commit `423f570`, "Merge branch 'main' into ...") since the
local branch last synced. That remote commit was a *second, independent*
merge of the same `e357fe5`/`d52e5f9` pair the Part B session had already
merged locally as `4b4b086` — same parents, different resolution. Diffing
the two merge results showed the difference was confined to `Hub.unity`:
the remote's merge was a naive auto-merge that kept **both** the no-visual
`LevelSelectKiosk` (Part B's placeholder placement) and Cameron's
with-Cylinder one, reintroducing the exact duplicate-kiosk problem Part B's
manual merge had deliberately resolved by removing the placeholder.
Resolved by merging origin in locally and keeping the deduplicated
`SceneRoots` entry (Cameron's kiosk, `fileID 1276837211`) over the
dangling duplicate — verified zero duplicate fileIDs and exactly one
`LevelSelectKiosk`/`LevelEditorAccessPoint` each afterward, then pushed.
No data was lost; this only affected `Hub.unity`. Worth knowing for next
session: something else (another session/branch) is also pushing to this
same branch — check `git log origin/<branch>` before assuming local state
is current.

---

## 2026-06-19 (Part D)

**Context:** "Look at my claude.md file and doc folder containing my game
structure. If there are no pending items, i want you to work on implementing
the timer system phase B." Audited `CLAUDE.md` + `docs/` for pending work
first, per the standing instruction. No Unity Editor available this session
either — all `.unity` changes hand-edited as Force-Text YAML and verified by
direct read/fileID cross-reference, **not compiled or opened in-Editor**.

### Audit result

One real pending item found: `docs/wiring/trowel-and-torch-tool-prefabs.md`
(Phase A material loop — duplicate Hammer.prefab into Trowel/WeldingTorch,
register in `DefaultNetworkPrefabs.asset`, wire into `ToolDepotSpawner`; pure
Editor wiring, no code). This blocks Phase A, not Phase B, so it's orthogonal
to the requested task rather than a blocker for it. Left untouched this
session — still pending, needs Cameron's Editor access to do the prefab
duplication.

No other pending items found. Proceeded to Timer System Phase B per the
conditional instruction.

### Timer System (Phase B) — implemented

See `docs/PLANNED_FEATURES.md` (Timer System section) and
`docs/ARCHITECTURE.md` (new "Timer System (Phase B)" section) for the full
write-up. Summary:

- **Skipped the nominal Contract System dependency.** `PLANNED_FEATURES.md`
  listed "Contract system (Phase B)" as a dependency for the timer, but
  `BlueprintData.contractDefaults` (`timeLimitSeconds`,
  `completionThresholds`) already existed in code and is already populated
  in all three blueprint JSONs (`blueprint_001.json`, `blueprint_new.json`,
  `blueprint_meow.json`). Read this directly instead of building
  `ContractData`/`ContractManager` — treated as using already-decided data
  plumbing, not inventing new design. The full Contract System (a manager
  object contract selection screens would write to) is still open.
- **New:** `LevelTimer` (`Assets/Scripts/Build/LevelTimer.cs`) — scene-placed
  `NetworkBehaviour`, mirrors the existing `LevelEditorBlueprintSync` pattern
  (own `NetworkObject`, no prefab registration needed). Seeds a
  `NetworkVariable<float>` from `contractDefaults.timeLimitSeconds` on
  spawn, ticks down server-side in `Update()`, and reacts to its own
  `OnValueChanged` (fires identically on host and clients, same pattern as
  `BuildTile._state.OnValueChanged`) to call
  `BuildSystem.EvaluateCompletion(forced: true)` exactly once when it
  crosses zero.
- **New:** `BuildSystem.EvaluateCompletion(bool forced = false)` +
  `LevelEnded` guard flag. Called from both `BuildTile.OnStateChanged` (natural
  completion, no `forced` arg) and `LevelTimer` (forced). Compares
  `CompletionPercent` against `contractDefaults.completionThresholds.full` and
  fires the new `GameEvents.OnLevelEnded(bool success, float
  completionPercent)`.
- **New:** `LevelTimerHUD` (`Assets/Scripts/Build/LevelTimerHUD.cs`) — plain
  `MonoBehaviour`, MM:SS countdown text, swaps to "Complete!" /
  "Time's Up — N%" on `GameEvents.OnLevelEnded`.
  - **Project rule applied:** `TextMeshProUGUI.m_RaycastTarget: 0` per
    CLAUDE.md's "All display TextMeshPro elements: Raycast Target =
    unchecked."
- **Deliberately not built:** payout calculation and the post-level scene
  transition (full "Win/Loss Conditions" feature) — both remain open in
  `PLANNED_FEATURES.md`, scoped to listen on the new `GameEvents.OnLevelEnded`
  event once built. Keeping this pass scoped to "timer ticks down, level-end
  evaluation fires" per the task name.
- **`Game1.unity` scene wiring (unverified, no Editor):** added a
  `LevelTimer` GameObject (NetworkObject + `LevelTimer` component, fileIDs
  `5200001`–`5200004`) and a `TimerCanvas` → `TimerText` hierarchy (screen-space
  overlay, top-center anchored, fileIDs `5200010`–`5200023`) modeled on the
  scene's existing `PauseCanvas` Canvas/CanvasScaler settings. Both added to
  `SceneRoots.m_Roots`. Verified zero duplicate fileIDs afterward
  (`grep -oP '^--- !u!\d+ &\K\d+' Assets/Scenes/Game1.unity | sort -n | uniq -c | sort -rn`
  — every fileID appears exactly once). **Please open `Game1.unity` in the
  Editor and confirm:** the timer text is positioned/sized sensibly, the
  countdown actually ticks and the level-end message swap reads correctly
  in a live test.
- New `.meta` files hand-created for both new scripts
  (`LevelTimer.cs.meta` guid `416a25d126334f5d9ac75f0e37e006b2`,
  `LevelTimerHUD.cs.meta` guid `0c7949acfed94b10a714b23ac4d735e2`).

### Open items for Cameron to review

- `docs/wiring/trowel-and-torch-tool-prefabs.md` still pending (Phase A,
  unrelated to this session, not actioned).
- `Game1.unity`'s new `LevelTimer`/`TimerCanvas` wiring is unverified —
  no Unity Editor available this session. Please open and playtest: timer
  counts down, HUD text reads correctly, level ends (success and forced-by-
  timer-expiry paths) trigger the right message.
- No compiler available either (`unity`/`dotnet`/`mcs`/`csc` all absent from
  this environment) — all new C# was manually re-read for syntax errors, not
  compiled. Please build once before relying on this.

### Follow-up: Level Summary UI

**Context:** "once the level completes event is triggered it should bring up
a level summary UI which eventually will have stuff like a blame summary on
who made the most mistakes or did the most or keeps track of deaths etc.
there should be a button to go back to the hub. don't worry about the blame
summary yet but add that to any relevant documentation already existing."

- **New:** `LevelSummaryUI` (`Assets/Scripts/Build/LevelSummaryUI.cs`) —
  listens for `GameEvents.OnLevelEnded` (the same event `LevelTimerHUD`
  already listens to), shows a panel with the result ("Level Complete" /
  "Time's Up") and completion %, unlocks the cursor. "Return to Hub" button
  uses the exact same `NetworkPlayer.RequestLoadSceneRpc("Hub")` relay
  `PauseMenu.LeaveToHub` uses (`NetworkSceneManager.LoadScene` is server-only,
  so clients route through their own `NetworkPlayer`), falling back to
  `MainMenu` if there's no NGO session.
- **Per explicit instruction, did NOT build the blame summary itself** (who
  made the most mistakes, deaths, etc.) — no stat-tracking exists anywhere in
  the codebase yet for this, and what counts as a "mistake" is an open design
  question, not mine to assume. Instead added a reserved, empty,
  inactive-by-default `BlameSummaryRoot` `RectTransform` inside `SummaryPanel`
  and documented it as the landing spot in both `docs/ARCHITECTURE.md`
  ("Level Summary UI" section) and `docs/PLANNED_FEATURES.md` (new "Level
  Summary" section, with open questions about what counts as a mistake and
  where per-player stat tracking should live).
- **`Game1.unity` scene wiring (unverified, no Editor):** added
  `SummaryCanvas` → `SummaryPanel` → `ResultText` / `CompletionText` /
  `ReturnToHubButton` / `BlameSummaryRoot` (fileIDs `5300010`–`5300071`),
  modeled on the existing `PausePanel`/`ResumeButton`/`LeaveToHubButton`
  hierarchy for consistent button/Image/Button component wiring. Added to
  `SceneRoots.m_Roots`. Verified zero duplicate fileIDs afterward and that
  every new fileID resolves to an actual document in the file.
- New `.meta` file hand-created: `LevelSummaryUI.cs.meta`, guid
  `ed1a624f94f847de916db0b166c9d32c`.
- **Unverified:** layout/positioning of the new summary panel, and that
  clicking "Return to Hub" actually works end-to-end — please playtest both
  the success and timer-expiry paths.

---

## 2026-06-19 (Part E)

**Context from Cameron:** three reported bugs — (1) leaving a blueprint level
back to the Hub brings the whole level along with the player, only the
players should return; (2) after returning to the Hub, the camera can't be
moved at all, mouse input does nothing; (3) the Level Editor staging area
should also be reachable from the kiosk terminal, not just the standalone
access point. No Unity Editor or C# compiler available this session either —
all changes hand-verified by direct read, **not compiled or opened in-Editor.**

### Bug 1 — level geometry follows players back to Hub (fixed)

Root cause: every dynamically-spawned `NetworkObject` in a level
(`BuildTile`/`OrderStation` in `BuildSystem.SpawnFromBlueprint()`, plus the
materials/tools spawned at runtime by `SupplyZoneSpawner`/`ToolDepotSpawner`/
`OrderQueueSystem`) never set `NetworkObject.DestroyWithScene`. NGO's default
for a *dynamically* spawned object (as opposed to one placed directly in the
scene file) is `DestroyWithScene = false` — intentional upstream behavior so
things like held items or player objects survive a scene change by migrating
into the next active scene instead of being destroyed. Since nothing in this
codebase ever opted out of that default, the entire level's tiles, depots,
stations, and any loose materials/tools rode along into `Hub` on every
`LoadSceneMode.Single` transition. Fixed by setting `DestroyWithScene = true`
immediately after every `Spawn()` call in those four files. Scene-placed
`NetworkObject`s (`LevelTimer`, `LevelEditorBlueprintSync`, `LevelSelectKiosk`,
`LevelEditorAccessPoint`, `StartingAreaTrigger`, `OrderQueueSystem` itself)
were never part of this bug — `IsSceneObject == true` objects are always
destroyed with their scene regardless of this flag.

### Bug 2 — mouse/camera dead after returning to Hub (fixed)

Root cause: `PlayerCamera` only re-locks the cursor and re-enables mouse look
when `GameEvents.OnGameStarted` fires. That event was only ever fired from
`HubPlayerState.ApplySpawnState`, gated on its `_isSpawnedIn` `NetworkVariable`
actually *changing* value — true on a player's first spawn into Hub, but the
player's `NetworkObject` is never destroyed/recreated across scene loads, so
on a return trip from `Game1`/`LevelEditor` (`PauseMenu.LeaveToHub` /
`LevelSummaryUI.ReturnToHub`, both of which unlock the cursor for their own
button clicks and never re-lock it), `_isSpawnedIn` was already `true` and
never changed again — nothing re-fired `OnGameStarted`, so the cursor and
mouse look stayed dead indefinitely. Fixed by having
`NetworkPlayer.OnActiveSceneChanged` re-fire `GameEvents.FireGameStarted()`
itself whenever `IsOwner && current.name == "Hub"`, independent of
`HubPlayerState`'s networked spawn-state tracking — this fires every time the
owner's active scene becomes Hub, not just the first time.

### Bug 3 — Level Editor not reachable from the kiosk (added)

`LevelSelectKiosk`'s menu gained a trailing "Enter Level Editor" option after
the scanned blueprint list (`OptionCount` is now `_availableBlueprintIds.Length
+ 1`; new `IsLevelEditorOption(index)` is true for the last row). Selecting it
calls a new `LevelSelectKiosk.EnterLevelEditorRpc()` — the same one-line
`NetworkManager.Singleton.SceneManager.LoadScene("LevelEditor", LoadSceneMode.Single)`
`LevelEditorAccessPoint.EnterLevelEditorRpc()` already does, duplicated rather
than shared so each Hub terminal stays self-contained (matching how
`LevelEditorAccessPoint` and `LevelSelectKiosk` were already independent of
each other). `PlayerInteraction.SelectKioskOption()` branches on
`IsLevelEditorOption(index)`. **Judgment call:** kept the standalone
`LevelEditorAccessPoint` terminal in place rather than removing it — Cameron's
phrasing ("should be in the kiosk **as well**") read as additive, not a
replacement. Flagging in case the intent was actually to consolidate down to
one access point.

### Changes made this session

- `Assets/Scripts/Build/BuildSystem.cs`, `SupplyZoneSpawner.cs`,
  `ToolDepotSpawner.cs`, `OrderQueueSystem.cs` — `DestroyWithScene = true` on
  every dynamically-spawned `NetworkObject` (Bug 1).
- `Assets/Scripts/NetworkPlayer.cs` — `OnActiveSceneChanged` re-fires
  `GameEvents.FireGameStarted()` on every return to Hub, not just first spawn
  (Bug 2).
- `Assets/Scripts/Build/LevelSelectKiosk.cs` — new trailing "Enter Level
  Editor" menu option + `EnterLevelEditorRpc()` (Bug 3).
- `Assets/Scripts/PlayerInteraction.cs` — `SelectKioskOption()` branches to
  the new RPC for that option.
- `docs/ARCHITECTURE.md` — documented all three fixes in place (Build Tiles,
  Player, Hub Blueprint-Select-and-Start Flow sections).

### Open items for Cameron to review

- **None of this session's changes have been compiled or run** — no Unity
  Editor or C# compiler available. Please open the project and confirm it
  still builds before relying on any of this.
- Playtest Bug 1: enter `Game1` (or `LevelEditor`), leave to Hub via both the
  Pause Menu and (for `Game1`) the Level Summary screen, confirm the Hub is
  clean of any leftover tiles/stations/materials/tools.
- Playtest Bug 2: same return trips, confirm mouse look and cursor lock work
  immediately on arrival in Hub with no extra input needed.
- Playtest Bug 3: confirm the kiosk's new last option enters the Level Editor
  for both host and non-host clients, same as the standalone access point.
- Confirm the "kept both access points" judgment call above is actually what
  was wanted, not a half-step toward consolidating to one.

## 2026-06-19 (Part F)

**Context from Cameron:** direct regression from Part E's Bug 2 fix — "when
you go in to the level editor now, the cursor is not available. if you press
pause, then it appears... when you click to place it only places in the
center of the screen, likely from the player cursor." No Unity Editor or C#
compiler available this session either — hand-verified by direct read only.

### Root cause

Part E's Bug 2 fix made `NetworkPlayer.OnActiveSceneChanged` re-fire
`GameEvents.FireGameStarted()` on every return to Hub, which re-locks the
cursor via `PlayerCamera.EnableMouseLook()`. That locked state now correctly
persists when leaving Hub for `LevelEditor` — `ApplyComponentState()` disables
the `playerCamera` component for `passiveScenes`, but `PlayerCamera.OnDisable()`
only unsubscribes from `GameEvents`; it never calls `DisableMouseLook()`. So
the cursor stayed locked to screen center the whole time the editor was open.
`LevelEditorController.HandlePlacement()` reads `Mouse.current.position` to
raycast placement (`LevelEditorController.cs:156`), which only reports a
real cursor position when the OS cursor is actually free — confirming this
was the click-only-registers-at-center symptom. Opening Pause unlocked the
cursor as a side effect (`PauseMenu.Pause()` sets `Cursor.lockState = None`
directly) without ever re-locking it, which is why pausing "fixed" it.

Before Part E's fix, the cursor happened to stay unlocked across the
Hub-return trip (the original Bug 2), and that broken state coincidentally
carried into LevelEditor in the shape it needed. Fixing Bug 2 correctly
removed that accident, exposing this gap.

### Fix

`NetworkPlayer.OnActiveSceneChanged` now also calls
`playerCamera.SetLookEnabled(false)` directly whenever `IsOwner &&
IsPassiveScene()` — symmetric to the existing Hub-entry branch that re-locks
it. `SetLookEnabled` is a plain public method on `PlayerCamera`, callable
regardless of the component's `enabled` flag, so this works even though
`ApplyComponentState()` disables `playerCamera` for the same scene in the
same call.

Checked for conflicts: `LevelEditorPreviewController` (the in-editor
walk-around preview mode) manages `Cursor.lockState` itself but is fully
self-contained and unrelated to `PlayerCamera`/`NetworkPlayer` — no overlap.

### Changes made this session

- `Assets/Scripts/NetworkPlayer.cs` — `OnActiveSceneChanged` now unlocks the
  cursor via `playerCamera.SetLookEnabled(false)` on entering any passive
  scene (currently just `LevelEditor`).
- `docs/ARCHITECTURE.md` — updated the Player section's Bug 2 note to cover
  this follow-up.

### Open items for Cameron to review

- **Still not compiled or run.** Please confirm the project builds.
- Playtest: enter the Level Editor from the Hub kiosk or the standalone
  access point, confirm the cursor is free and click-to-place lands where
  the cursor actually is, with no need to open/close Pause first.
- Playtest: leave Level Editor back to Hub, confirm mouse look still
  re-engages correctly (this path was already covered by Part E but is worth
  re-checking given the new code right next to it).
