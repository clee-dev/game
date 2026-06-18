# Crazy Construction

A first-person co-op party game for 1-4 players on PC (Steam). Players receive a
blueprint defining a structure to build, gather materials, carry them to build tiles,
and use tools to place them. Chaos events, contracts, and a rogue-lite economy
create escalating pressure across a run.

**Collaborators:** Cameron Lee (lead), Kyle Wong (design), Gilbert Zavala (art)

---

## Tech Stack

- Unity 6, URP 17.3.0
- Netcode for GameObjects 2.12.0
- Facepunch Steamworks — Steam lobbies + P2P via custom `FacepunchTransport`
- Vivox 16.11.0 + UGS (Authentication, Core) — proximity voice
- Newtonsoft.Json 3.2.1 — blueprint serialization
- Input System 1.19.0
- ProBuilder 6.0.9, AI Navigation 2.0.12 (present, not yet used in gameplay)

---

## Docs (read in this order at session start)

- `docs/SESSION.md` — what was in progress last session, open questions, next steps
- `docs/ARCHITECTURE.md` — implementation source of truth; what exists, how it works
- `docs/GAME_INTENT.md` — design intent; why mechanics exist, not just how they work
- `docs/wiring/` — pending Unity Editor wiring tasks
- `docs/wiring/done/` — completed wiring tasks (do not re-read these)

---

## Rules

- Read `docs/SESSION.md` first, every session. If it is missing, note that and continue.
- `docs/ARCHITECTURE.md` is kept accurate and up to date as you make changes.
  Write it for your future self reading it cold next session.
- `docs/GAME_INTENT.md` is **read-only** unless Cameron explicitly tells you to update it.
  If an implementation decision changes the *intent or purpose* of a mechanic (not just
  how it works), flag it and ask before touching that file.
- Do not combine wiring docs into one file. One file per feature.
- Do not delete or modify existing wiring docs. Cameron moves completed ones to
  `docs/wiring/done/` manually.
- Save progress to `docs/` incrementally during long tasks.
- Do not stop early due to token concerns. The context window will compact and you
  can continue from your saved state.
- Never assume design decisions from adjacent mechanics. If something is ambiguous,
  ask.

---

## Code Style

- Server-authoritative for all gameplay-critical state. Client-predicted for movement
  and held-object positions only.
- Use `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]`
  (not deprecated `[ServerRpc(RequireOwnership = false)]`).
- Use `Keyboard.current?.key.wasPressedThisFrame` not legacy `Input.GetKey`.
- All material properties in `CBUFFER_START(UnityPerMaterial)` for SRP Batcher.
- `ClientNetworkTransform.Authority Mode = Owner` must be set in the Inspector;
  the `OnIsServerAuthoritative()` code override is ignored in Unity 6.
- All display TextMeshPro elements: **Raycast Target = unchecked**.
