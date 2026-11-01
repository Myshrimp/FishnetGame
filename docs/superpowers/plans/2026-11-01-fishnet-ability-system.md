# FishNet Predicted Ability System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GAS-like predicted ability runtime and migrate charge plus projectile interaction into the player's FishNet replicate/reconcile loop.

**Architecture:** A pure C# runtime processes fixed-capacity ability commands and snapshots inside `PlayerControllerPrediction`. Owner prediction transactions use monotonic ids; server decisions ride back in reconcile snapshots. Projectile world changes remain server-only while charge state is fully predicted.

**Tech Stack:** Unity 2022.3.62f3, C#, FishNet 4.7.2, NUnit EditMode tests

**Spec:** `docs/superpowers/specs/2026-11-01-fishnet-ability-system-design.md`

## Global Constraints

- Do not add package dependencies or replace FishNet prediction.
- Predicted duration and cooldown logic uses integer simulation steps and `TimeManager.TickDelta`, never `Time.time`.
- Do not emit cues, mutate server-owned projectile state, or add pending transactions during replay.
- Preserve server-authoritative projectile collision, hit, stun, and damage behavior.
- Keep runtime state and network payloads fixed-capacity and allocation-free per simulation tick.

## Review Focus

- Prediction-id wraparound must preserve monotonic ordering and decision lookup.
- More than one edge input in a rendered frame must survive into the next replicate.
- A rejected activation must restore resource, tags, active state, and cooldown through reconcile.
- Missing intermediate reconcile packets must still resolve pending predictions using the decision watermark and bitmap.
- Stun/carry arriving while charge is active must cancel charge identically on owner, server, and replay.

---

### Task 1: Ability value types and command buffer

**Files:**
- Create: `Assets/Scripts/Abilities/GameplayTag.cs`
- Create: `Assets/Scripts/Abilities/AbilityTypes.cs`
- Create: `Assets/Tests/Editor/PredictedAbilityRuntimeTests.cs`

**Interfaces:**
- Produces: `GameplayTag`, `AbilityId`, `AbilityActivationMode`, `AbilityInputPhase`, `AbilityCommand`, `AbilityCommandBuffer`, `AbilitySpec`, `AbilityCueEvent`.

- [x] **Step 1: Write failing command-buffer and decision-history tests**

Test that three commands retain order, a fourth is rejected, reset empties the buffer, and unsigned prediction ids compare correctly across wraparound.

- [x] **Step 2: Run the EditMode test and verify missing types fail compilation**

Run the Unity EditMode suite for `FaceSlapper.Projectile.Tests`; expect compile errors naming the new ability types.

- [x] **Step 3: Implement serializable fixed-capacity value types**

Add `bool AbilityCommandBuffer.TryAdd(AbilityCommand value)`, `AbilityCommand Get(int index)`, and `void Clear()`. Use three explicit fields rather than arrays or lists.

- [x] **Step 4: Run the focused tests and verify they pass**

- [x] **Step 5: Commit the value types and tests**

### Task 2: Predicted ability runtime

**Files:**
- Create: `Assets/Scripts/Abilities/AbilityDecisionHistory.cs`
- Create: `Assets/Scripts/Abilities/AbilitySystemSnapshot.cs`
- Create: `Assets/Scripts/Abilities/PredictedAbilityRuntime.cs`
- Modify: `Assets/Tests/Editor/PredictedAbilityRuntimeTests.cs`

**Interfaces:**
- Consumes: Task 1 ability value types.
- Produces: `IAbilityResource`, `AbilityExecutionContext`, `PredictedAbilityRuntime.AdvanceTick`, `TryActivate`, `CaptureSnapshot`, `Reconcile`, `HasTag`, and `Cue`.

- [ ] **Step 1: Add failing runtime tests**

Cover required/blocked tags, stamina cost, cooldown decrement, toggle tags, external-tag cancellation, first-execution cue emission, replay cue suppression, accepted decision confirmation, rejected decision rollback, skipped reconcile packets, and prediction-id wraparound.

- [ ] **Step 2: Run tests and verify failures reference unimplemented runtime behavior**

- [ ] **Step 3: Implement four-slot snapshot state and 32-entry local pending ledger**

Use a 64-bit rejection window plus highest-processed watermark. Reconcile runtime state before FishNet replay but retain pending ids newer than the watermark.

- [ ] **Step 4: Run focused tests and verify all runtime cases pass**

- [ ] **Step 5: Commit the runtime**

### Task 3: Integrate abilities with player prediction

**Files:**
- Modify: `Assets/Scripts/Network/PlayerControllerPrediction.cs`
- Modify: `Assets/Tests/Editor/PlayerStunPredictionTests.cs`

**Interfaces:**
- Consumes: `PredictedAbilityRuntime`, `AbilityCommandBuffer`, and `AbilitySystemSnapshot`.
- Produces: Q/E/mouse command capture, ability snapshot reconciliation, stamina adapter, compatibility `State.IsCharging`, and public cue event.

- [ ] **Step 1: Add failing motor integration tests**

Test charge activation, replay idempotence, stun cancellation, reconcile restoration, simultaneous buffered inputs, and no predicted stamina underflow.

- [ ] **Step 2: Run focused motor tests and verify failure**

- [ ] **Step 3: Add ability commands to `ReplicateData` and snapshots to `ReconcileData`**

Allocate prediction ids while buffering edge inputs in `Update`. Advance the runtime once at the start of every replicate, process commands in order, and mirror the charging tag into `State.IsCharging`.

- [ ] **Step 4: Implement `IAbilityResource` with existing stamina methods**

Refresh stunned/carried external tags when those states change. Preserve gravity, knockback, carry, and future-input behavior.

- [ ] **Step 5: Run motor and runtime tests and verify they pass**

- [ ] **Step 6: Commit player integration**

### Task 4: Migrate projectile interaction activations

**Files:**
- Modify: `Assets/Scripts/Network/PlayerProjectileController.cs`
- Modify: `Assets/Scripts/Network/PlayerControllerPrediction.cs`
- Create: `Assets/Tests/Editor/PlayerAbilityProjectileTests.cs`

**Interfaces:**
- Consumes: projectile-interact and projectile-throw commands emitted by the motor.
- Produces: non-mutating client validation and server-only `TryExecuteInteraction` / `TryExecuteThrow` methods.

- [ ] **Step 1: Add failing migration tests**

Prove client execution never changes `_heldObject`, server pickup/drop/throw mutates once, unavailable targets reject activation, and replay cannot repeat the world mutation.

- [ ] **Step 2: Run the focused tests and verify current ServerRpc path fails expectations**

- [ ] **Step 3: Remove input polling and ServerRpc activation from `PlayerProjectileController`**

Move cooldown ownership to ability specs. Keep overlap validation, held-object SyncVar, and server cleanup.

- [ ] **Step 4: Dispatch projectile commands from the motor's replicate executor**

Pass aim direction through `AbilityCommand`; only `IsServerStarted` may call mutation methods.

- [ ] **Step 5: Run ability, motor, projectile prediction, and presentation tests**

- [ ] **Step 6: Commit projectile migration**

### Task 5: Whole-feature verification and documentation

**Files:**
- Modify: `docs/superpowers/specs/2026-11-01-fishnet-ability-system-design.md` only if implementation details changed.
- Modify: `docs/superpowers/plans/2026-11-01-fishnet-ability-system.md` to mark completed steps.

**Interfaces:**
- Consumes: all previous tasks.
- Produces: verified milestone and operator-facing architecture notes.

- [ ] **Step 1: Run the complete EditMode suite**

Expect all `FaceSlapper.Projectile.Tests` tests to pass with zero failures.

- [ ] **Step 2: Search for stale activation paths**

Verify Q charge no longer uses `ChargePressed`, migrated projectile interaction has no `ServerRpc`, predicted cooldown does not use `Time.time`, and every replay-sensitive cue is guarded.

- [ ] **Step 3: Inspect git diff for prefab or generated-file noise**

Only source, tests, metas, and design/plan documents should change.

- [ ] **Step 4: Commit final documentation and verification adjustments**
