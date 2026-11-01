# FishNet Predicted Ability System Design

## Intent

Build a small, reusable GAS-like ability layer on top of the project's existing FishNet 4.7.2 prediction loop. The first milestone must make charge and projectile interaction use the same activation pipeline while preserving server-authoritative hits and the existing movement/projectile prediction behavior.

## Goals

- Identify every activation with a monotonically increasing `PredictionId`.
- Predict owner-side activation, tags, resource cost, cooldown, and cues immediately.
- Let the server accept or reject the same command during the FishNet replicate simulation.
- Carry decisions in reconcile state so the owner can confirm or reject predicted cues without a second RPC protocol.
- Rewind ability state together with movement state, then let FishNet replay later commands.
- Support required/blocked/granted tags, toggle and instant abilities, stamina cost, and tick-based cooldown.
- Migrate charge and pickup/drop/throw into the ability command path.
- Keep projectile hit resolution and damage server authoritative.

## Non-goals

- A complete Unreal GAS clone.
- Arbitrary runtime attributes, effect stacking, effect immunity, executions, or hierarchical string tags.
- Predicting projectile ownership or damage on the client.
- Replacing FishNet prediction, state forwarding, or graphical smoothing.
- Supporting more than four configured ability slots in this milestone.

## Constraints

- Unity 2022.3.62f3 and FishNet 4.7.2.
- No new package dependencies.
- Simulation uses `TimeManager.TickDelta` and integer step counters; predicted logic must not use `Time.time`.
- No RPC, spawning, audio, or visual side effect may repeat during replay.
- The current `gas` branch is the implementation branch. Work remains in the existing checkout because Unity-generated state is not available in a separate linked worktree.

## Approaches

### Selected: ability runtime embedded in the existing player prediction loop

Use a pure C# `PredictedAbilityRuntime` owned by `PlayerControllerPrediction`. Ability commands travel inside the player's existing `ReplicateData`; ability snapshots travel inside `ReconcileData`. This guarantees movement and ability state rewind at the same FishNet tick and avoids ordering bugs between separate predicted `NetworkBehaviour` instances.

### Rejected: independent predicted NetworkBehaviour

Giving abilities their own `[Replicate]` and `[Reconcile]` methods is modular, but FishNet does not promise a semantic ordering between the motor and ability behaviours. Charge, stun, carry, stamina, and movement would need cross-behaviour tick coordination.

### Rejected: ServerRpc-only ability component

This is simple and resembles the current projectile interaction controller, but activation remains round-trip delayed and cannot provide GAS-like prediction, rejection, or replay behavior.

## Architecture

### Ability model

`AbilitySpec` is immutable runtime configuration: id, activation mode, required tags, blocked tags, granted tag, stamina cost, and cooldown steps. The first player configuration contains charge toggle, projectile interact, and projectile throw.

`GameplayTag` is a 64-bit flags enum. The initial tags cover stunned, carried, charging, and per-ability cooldown tags. External tags are derived each simulation step from authoritative/predicted player status; owned tags and slot state are captured in the ability snapshot.

`AbilityCommandBuffer` carries up to three commands per replicate so Q, E, and throw pressed during one rendered frame are not lost. Each command contains ability id, input phase, `PredictionId`, and aim direction.

### Prediction transaction model

The owner allocates `PredictionId` when an edge input is buffered. On its first normal execution, accepted commands are added to a local pending ledger and emit a `Predicted` cue. Replay changes simulation state but never adds another ledger entry or cue.

The server records every accepted or rejected command in `AbilityDecisionHistory`. It contains a highest-processed watermark and a 64-bit rejection bitmap. Reconcile applies this history: pending ids at or below the watermark are confirmed unless their rejection bit is set. This avoids per-activation RPCs and tolerates unreliable reconcile packets.

### State and replay

`AbilitySystemSnapshot` contains initialized/version state, owned tags, four fixed slot states, and decision history. Applying it never destroys still-pending newer transactions. FishNet then replays commands newer than the reconcile tick, rebuilding active tags, cooldowns, and resource changes.

Stamina remains in `PlayerControllerPrediction.ReconcileData`; the runtime accesses it through an `IAbilityResource` adapter implemented by the motor. A rejected server activation therefore corrects both ability state and stamina in the same reconcile.

### Gameplay execution

Framework validation checks tags, cooldown, and cost before calling a host executor. Charge is a pure predicted toggle. Projectile interact/throw performs a non-mutating local validation on clients and the real pickup/drop/throw only on the server. Failure of server target validation records rejection.

Projectile flight remains in `NetworkProjectile`. Its existing transition sequence and predicted rigidbody are not replaced by player `PredictionId`.

### Cues

`AbilityCueEvent` reports `Predicted`, `Confirmed`, `Rejected`, `Activated`, and `Ended`. Logical state changes always replay; cue emission is guarded to first normal execution or decision processing. The milestone exposes the event API without adding new art or audio.

## Migration

- Q becomes a charge ability command rather than a bespoke `ChargePressed` motor field.
- E becomes projectile-interact; mouse-left becomes projectile-throw.
- `PlayerProjectileController` stops polling input and sending ServerRpcs. It exposes validation and server execution methods called from the player's replicate.
- `State.IsCharging` remains as a compatibility mirror of the charging gameplay tag for current carry/projectile code.

## Verification

- Pure EditMode tests cover activation, tag blocking, cooldown, resource cost, decision confirmation/rejection, packet loss windows, replay cue suppression, and snapshot restore.
- Existing stun tests prove stun cancels charge and reconciliation remains deterministic.
- Projectile tests continue proving release idempotence, collision grace rewind, and presentation smoothing.
- Static inspection verifies all input actions use the new ability command path and no migrated predicted cooldown uses `Time.time`.
