# Wiring: Steel — Second Two-Person Carry Point (Symmetric Redesign)

**Status:** Code complete (`TwoPersonCarry`, `TwoPersonCarryPoint.pointIndex` — see
`docs/ARCHITECTURE.md` Interaction & Pickup section, "Two-player shared carry binding —
symmetric two-point redesign"). Editor wiring needed: one new child object on
`Steel.prefab`, plus a field set on each of its two attach points.

**Supersedes the attach-point portion of `docs/wiring/steel-material-and-welding-torch.md`**
(its "Steel.prefab — add" item 3, the single attach-point child). That doc is left
unmodified per project convention; this doc covers only what changed.

---

## Why

Cameron playtested the original single-attach-point design (one fixed "primary," carried
by picking up the beam's main body collider, plus one dedicated `TwoPersonCarryPoint` for a
second player binding in) and reported it should instead be fully symmetric: either player
grabs either of two interchangeable points, and the *other* player has to use the other one.
The first grab (on an unheld beam) becomes a normal solo carry; a second grab by someone
else makes it shared. Generic body-collider pickup is now disabled in code for any item
with a `TwoPersonCarry` component — `Steel.prefab`'s main `BoxCollider` (on the root, sibling
of `Rigidbody`) no longer does anything for pickup purposes; both attach points are
mandatory now, not optional.

This pass also fixed two related bugs (rotation jank while a stationary carrier looks
around, and no maximum carry distance) — pure code changes, nothing for Cameron to wire for
those.

---

## `Steel.prefab` — what to change

The prefab already has one `TwoPersonCarryPoint` child (added per the original wiring doc):
a `GameObject` at local position `{0.444, 0, 0}`, sibling of the `Cube` mesh child, with a
`BoxCollider` (`size {0.31, 1, 1}`) and a `TwoPersonCarryPoint` component whose `carry`
points at the `TwoPersonCarry` component on the prefab root.

1. **On that existing attach point**, set the new `pointIndex` field to **0**.
2. **Duplicate it** (or build a fresh matching child from scratch) to create a second attach
   point:
   - Local position **mirrored** across the beam's center: `{-0.444, 0, 0}` (the beam root's
     `m_LocalScale` is `{1.87, 1, 1}` — elongated along local X — so `0.444`/`-0.444` are the
     two ends, matching how the first point was placed).
   - Same `BoxCollider` size (`{0.31, 1, 1}`) — needs its own collider, separate from the
     first point's and from the main pickup collider, same reasoning as the original wiring
     doc (`TwoPersonCarryPoint`'s own doc comment: the raycast needs to resolve each attach
     point as a distinct target).
   - `TwoPersonCarryPoint.carry` → the same `TwoPersonCarry` component on the prefab root
     (if duplicated within the prefab, this reference should carry over automatically —
     just confirm it in the Inspector rather than re-assigning blind).
   - `TwoPersonCarryPoint.pointIndex` → **1**.
3. Rename both attach-point children to something clearer than the default `GameObject`
   (e.g. `AttachPointA` / `AttachPointB`) if you want — purely cosmetic, no functional
   requirement either way.

No other component on `Steel.prefab` needs to change. `TwoPersonCarry.forwardOffset` (0.6) /
`heightOffset` (1.2) and the new `maxShareDistance` (4, script default) are all tunable in
the Inspector once you can see the beam in-game — nothing mandatory to change before testing.

---

## Suggested playtest once wired

- Either attach point, grabbed first while the beam is unclaimed, starts a normal solo
  carry (prompt reads `[E] Pick Up`) and drops movement to ~25% speed.
- A second player grabbing the *other* point while the first is already carrying binds in
  as a shared carrier (prompt reads `[E] Help Carry`); both players return to full speed.
- Either carrier can release independently (`[E]`): the non-owner releasing leaves the owner
  still carrying solo; the owner releasing while shared hands the beam to the other carrier
  instead of dropping it — confirm the newly-promoted owner can still walk it somewhere and
  drop/throw it themselves afterward (this is the handoff-reconciliation fix from this pass).
- Standing still and just looking around (mouse only, no movement) while sharing a carry no
  longer visibly rotates the beam.
- Walking far enough apart while sharing (~4m, `maxShareDistance`) auto-releases the
  non-owner side without either player pressing anything.
