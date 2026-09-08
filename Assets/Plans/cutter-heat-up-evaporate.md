# Project Overview

- **Game Title:** Space Salvager
- **High-Level Concept:** A networked multiplayer game where players use tools (a cutter and a gravity gun) to dismantle and salvage modular spaceships built from welded `SpaceshipPart` grids.
- **Players:** Networking multiplayer (Unity Netcode for GameObjects 2.13.2, server-authoritative host model).
- **Inspiration / Reference Games:** Salvage/dismantling sandbox games; ship-breaking simulators.
- **Tone / Art Direction:** Sci-fi, zero-g space environment.
- **Target Platform:** StandaloneWindows64 (PC).
- **Screen Orientation / Resolution:** Landscape.
- **Render Pipeline:** URP (Universal RP 17.5.0), shaders use `Universal RP/Lit` with a `_BaseColor` property.

# Feature Summary

Change the `ToolCutter` secondary action from an **instant** evaporation into a **hold-to-heat-then-evaporate** mechanic:

- While the player holds Secondary and aims at a `SpaceshipPart`, the part gradually "heats up" and only evaporates once fully heated.
- Time-to-evaporate scales with the part's calculated `PartMass` and the difference between the tool's `cutStrength` and the part's `cutResistance`.
- If `cutResistance >= cutStrength`, the cutter has **no effect** on that part.
- Releasing Secondary (or looking away) makes the part **cool down**, and cool-down is **faster** than heat-up, returning to the part's original color.
- Heat is visualized by the material's `_BaseColor` lerping increasingly toward red.
- The effect is **server-authoritative** and replicated so **all clients** see the heating.
- **Multiple clients** cutting the same part **stack** their contributions, heating it faster (each by their own `cutStrength`).

# Game Mechanics

## Core Gameplay Loop

Aim cutter → hold Secondary → beam heats the targeted part over time (visibly reddening) → part evaporates when fully heated → optionally sever/split the ship → repeat to salvage. Cooperative players can gang up on tough (high-resistance / high-mass) parts to break them faster.

## Controls and Input Methods

Unchanged input bindings (New Input System). Secondary action is routed through the existing pipeline:
`PredictedPlayerController` binds `SecondaryInput.action.performed → tools[currentTool].PressSecondary()` and `.canceled → tools[currentTool].ReleaseSecondary()`.

The only behavioral change is that Secondary is now a **held** action for the cutter: `PressSecondary` starts heating, `ReleaseSecondary` stops it, and an owner-side `Update` loop drives continuous re-aiming while held (mirroring `ToolGravitygun`'s `isCharging` pattern).

# UI

No new UI. Feedback is entirely diegetic via the part's `_BaseColor` reddening as it heats. (Optional future extension: a crosshair heat gauge — out of scope here.)

# Design Decisions (confirmed with user)

1. **Sync model:** Server-authoritative with throttled replication. Clients send lightweight heartbeats ("I'm heating part X with strength S") ~10 Hz; the server owns heat accumulation and the evaporation decision, and broadcasts the current heat level ~10 Hz. Clients smoothly lerp the visual. Robust against grid splits and join-in-progress; consistent with existing `EvaporatePartClientRpc(partId)` / gravity-gun `Start/StopHolding` patterns.
2. **Evaporation-time formula:** `heatNeeded = PartMass * heatPerMass`. Heat-per-second = sum over active contributors of `max(0, cutStrength - cutResistance)`. If total rate is 0 (all cutters at/below resistance), the part does not heat. Denser/bigger parts take longer; more/stronger cutters go faster.
3. **Visualization:** Lerp `_BaseColor` toward red using a `MaterialPropertyBlock` (no material-instance leak, unlike the project's current `.material.SetColor` usage).
4. **Cool-down feel:** Cooling is **faster** than heating and returns the part to its **original** base color.

# Key Asset & Context

## Files to modify

- `Assets/Scripts/ToolCutter.cs`
  - Existing: `[SerializeField] private int cutStrength`, `PressSecondary()` (currently calls `spaceshipPart.EvaporatePart()` instantly), empty `ReleaseSecondary()`, `TryGetLookedAtSpaceshipPart(out SpaceshipPart)`, `[SerializeField] private Transform playerCamera`, evaporate raycast fields.
  - `PressSecondary()` currently:
    ```csharp
    public override void PressSecondary()
    {
        if (!IsOwner) return;
        if (TryGetLookedAtSpaceshipPart(out SpaceshipPart spaceshipPart))
            spaceshipPart.EvaporatePart();   // <-- instant; to be replaced
    }
    ```

- `Assets/Scripts/SpaceshipPart.cs`
  - Existing: `NetworkBehaviour`, `public int SpaceshipPartId`, `private MeshRenderer meshRenderer`, `[SerializeField] private int cutResistance`, `public float PartMass`, and the established per-part RPC pattern:
    ```csharp
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void EvaporatePartServerRpc() { ExecuteEvaporatePart(); }
    ```
    (Confirms per-part Server RPCs work despite parts sharing the grid's `NetworkObject`.)
  - `ExecuteEvaporatePart()` resolves the parent grid via `GetComponentInParent<SpaceshipGrid>()` and calls `parentGrid.EvaporatePart(this)` — this is the existing, correct evaporation entry point we will reuse.

- `Assets/Scripts/SpaceshipGrid.cs` (reference only, likely no change)
  - `public void EvaporatePart(SpaceshipPart part)` is server-authoritative: detaches, splits disconnected clusters, calls `EvaporatePartClientRpc(partId)`, destroys the part, recalculates physics. We reuse it unchanged via `SpaceshipPart.ExecuteEvaporatePart()`.

## Rendering context

- URP `Universal RP/Lit`; base color property is `_BaseColor`. We cache `Shader.PropertyToID("_BaseColor")`.
- Use `MeshRenderer.GetPropertyBlock` / `SetPropertyBlock` with a `MaterialPropertyBlock` — no per-instance material creation, no leaks.
- Capture the original base color once per peer (from `meshRenderer.sharedMaterial.GetColor(_BaseColor)`); when heat returns to 0, clear the override.

# Networking Design (detailed)

Because a `SpaceshipPart` has no `NetworkObject` of its own (parts are children of the `SpaceshipGrid`, addressed by `SpaceshipPartId`), but the existing code already declares working per-part `[Rpc]` methods (`EvaporatePartServerRpc`), we place the new heat RPCs directly on `SpaceshipPart`, matching the existing pattern. Parts are **not** reparented during heating (only evaporation triggers splits), so part-level RPCs are safe for this feature.

## Heartbeat + timeout model (avoids explicit stop RPCs and disconnect edge-cases)

**Client (cutter owner):**
- `PressSecondary` sets `isHeating = true` and sends an immediate heartbeat.
- Owner-side `Update`: while `isHeating`, every `contributionSendInterval` (~0.1s), raycast via existing `TryGetLookedAtSpaceshipPart`. If a part is hit, call `part.HeatContributionServerRpc(cutStrength)`.
- `ReleaseSecondary` sets `isHeating = false` (simply stops heartbeats — the server times the contributor out).

**Server (`SpaceshipPart`):**
- `HeatContributionServerRpc(int strength, RpcParams rpcParams = default)` records `contributors[senderClientId] = { strength, lastSeen = Time.time }` using `rpcParams.Receive.SenderClientId`.
- Server-only `Update`:
  - Purge contributors whose `lastSeen` is older than `contributorTimeout` (~0.3s) — this cleanly handles release, looking away, switching targets, and client disconnects.
  - `totalRate = Σ max(0, strength - cutResistance)` over remaining contributors.
  - `heatNeeded = Mathf.Max(epsilon, PartMass * heatPerMass)`.
  - If `totalRate > 0`: `heat01 += (totalRate / heatNeeded) * dt`; else `heat01 -= coolSpeed01 * dt` (cool-down faster).
  - Clamp `heat01` to `[0,1]`. If `heat01 >= 1` → call `ExecuteEvaporatePart()` (reuses existing grid evaporation) and stop.
  - Throttle (~`heatBroadcastInterval` 0.1s) a broadcast `UpdateHeatClientRpc(heat01)` (`SendTo.Everyone`) when the value changed meaningfully.

**All peers (`SpaceshipPart`):**
- `UpdateHeatClientRpc(float heat01)` stores `targetHeat01`.
- Client-side `Update` lerps `displayHeat01 → targetHeat01` for smoothness and applies the color via `MaterialPropertyBlock`: `Color.Lerp(originalBaseColor, hotColor, displayHeat01)`. When `displayHeat01` reaches ~0, clear the property override to restore the exact original material.

## New serialized tunables

- `ToolCutter`: `contributionSendInterval` (default 0.1s). Reuses existing `cutStrength`, `playerCamera`, and evaporate raycast fields.
- `SpaceshipPart` (new `[Header("Heat / Cutting")]`): `heatPerMass` (k, default 1), `coolSpeed01` (per-second, default e.g. 1.5 so cooling is faster than typical heating), `contributorTimeout` (0.3s), `heatBroadcastInterval` (0.1s), `hotColor` (default `Color.red`).

# Implementation Steps

### Step 1 — Add heat state, RPCs, and accumulation loop to `SpaceshipPart`
- **Description:** In `Assets/Scripts/SpaceshipPart.cs`, add: `[Header("Heat / Cutting")]` serialized tunables (`heatPerMass`, `coolSpeed01`, `contributorTimeout`, `heatBroadcastInterval`, `hotColor`); a server-only `Dictionary<ulong, (int strength, float lastSeen)>` of contributors; runtime `heat01`, `targetHeat01`, `displayHeat01`; cached `MaterialPropertyBlock`, `originalBaseColor`, and `_BaseColor` id. Implement `HeatContributionServerRpc(int strength, RpcParams rpcParams = default)`. Add server-side heat accumulation/cooldown/purge in `Update`, calling the existing `ExecuteEvaporatePart()` at `heat01 >= 1`. Add `UpdateHeatClientRpc(float heat01)` (`SendTo.Everyone`) and client-side lerp+`MaterialPropertyBlock` color application (capture `originalBaseColor` from `sharedMaterial` in `OnEnable`/`Start`; clear override at 0). Do **not** remove existing evaporate/sever methods.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No (Step 2 depends on the new RPC signature)

### Step 2 — Convert `ToolCutter` secondary to hold-to-heat
- **Description:** In `Assets/Scripts/ToolCutter.cs`, replace the instant `spaceshipPart.EvaporatePart()` call in `PressSecondary()` with `isHeating = true` + an immediate heartbeat send. Add an owner-only `Update` that, while `isHeating`, throttles by `contributionSendInterval` and calls `part.HeatContributionServerRpc(cutStrength)` on the currently aimed part (via existing `TryGetLookedAtSpaceshipPart`). Implement `ReleaseSecondary()` to set `isHeating = false`. Keep the `IsOwner` guards.
- **Assigned role:** developer
- **Dependencies:** Depends on Step 1 (RPC signature `HeatContributionServerRpc(int)`)
- **Parallelizable:** No

### Step 3 — Compile & inspector wiring verification
- **Description:** Confirm the project compiles (no errors in Console) and that the new `SpaceshipPart` tunables appear on the `pfb_placeholder_metalwall` prefab and scene part instances with sensible defaults (`hotColor` = red, `heatPerMass` = 1, `coolSpeed01` faster than heat). No prefab reference rewiring is expected since we reuse existing fields; verify `cutStrength` on `tool_cutter.prefab` and `cutResistance` on parts are set.
- **Assigned role:** developer
- **Dependencies:** Depends on Steps 1 & 2
- **Parallelizable:** No

# Verification & Testing

**Compile/static:**
- No compile errors; no new `NetworkVariable` added to parts; RPC attribute syntax matches Netcode 2.x (`[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]`, `[Rpc(SendTo.Everyone)]`).

**Single-player (host) manual checks:**
1. Aim cutter at a part with `cutStrength > cutResistance`, hold Secondary → part reddens gradually, then evaporates (reusing existing split/despawn). ✅
2. Release Secondary mid-heat → part cools back to original color **faster** than it heated. ✅
3. Aim at a part with `cutResistance >= cutStrength` → no reddening, never evaporates. ✅
4. Heavier/denser part (higher `PartMass`) visibly takes longer than a light part with the same tool. ✅
5. Look away mid-heat (contributor timeout) → cools down. ✅

**Multiplayer (Multiplayer Play Mode) checks:**
6. Client A heats a part → Client B (and host) see the same reddening in near-real-time. ✅
7. Clients A and B both heat the same part → it heats **faster** than one alone (rates stack by each `cutStrength`), and both see identical visuals. ✅
8. A heating client disconnects → server purges its contribution via timeout; part cools if no other contributors. ✅
9. Evaporation still correctly severs/splits the ship into new grids where applicable (existing `SpaceshipGrid.EvaporatePart` path). ✅

**Edge cases:**
- `PartMass == 0` guarded via `Mathf.Max(epsilon, ...)` to avoid divide-by-zero / instant evaporation.
- Part evaporates while other contributors still active → no null-ref (object destroyed, RPCs cease).
- Rapid target switching while holding → old target times out and cools, new target heats.
