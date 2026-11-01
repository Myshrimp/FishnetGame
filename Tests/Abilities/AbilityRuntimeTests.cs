using System;
using System.Collections.Generic;
using FaceSlapper.Abilities;
using UnityEngine;

public static class AbilityRuntimeTests
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        Check(nameof(CommandBufferPreservesEveryEdgeInputInOrder), CommandBufferPreservesEveryEdgeInputInOrder);
        Check(nameof(PredictionSequenceOrdersIdsAcrossWraparound), PredictionSequenceOrdersIdsAcrossWraparound);
        Check(nameof(ActivationAppliesTagsCostAndCooldown), ActivationAppliesTagsCostAndCooldown);
        Check(nameof(ExternalBlockedTagRejectsAndCancelsCharge), ExternalBlockedTagRejectsAndCancelsCharge);
        Check(nameof(ReplayRebuildsStateWithoutRepeatingCues), ReplayRebuildsStateWithoutRepeatingCues);
        Check(nameof(ReconcileConfirmsAndRejectsSkippedDecisions), ReconcileConfirmsAndRejectsSkippedDecisions);
        Check(nameof(DecisionHistorySurvivesPredictionIdWraparound), DecisionHistorySurvivesPredictionIdWraparound);

        Console.WriteLine($"Ability runtime tests: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void CommandBufferPreservesEveryEdgeInputInOrder()
    {
        AbilityCommandBuffer buffer = default;
        Assert(buffer.TryAdd(Command(AbilityId.Charge, 41)), "first input was rejected");
        Assert(buffer.TryAdd(Command(AbilityId.ProjectileInteract, 42)), "second input was rejected");
        Assert(buffer.TryAdd(Command(AbilityId.ProjectileThrow, 43)), "third input was rejected");
        Assert(!buffer.TryAdd(Command(AbilityId.Charge, 44)), "overflow input was silently accepted");
        Assert(buffer.Count == 3, "buffer count changed after overflow");
        Assert(buffer.Get(0).PredictionId == 41, "first input order changed");
        Assert(buffer.Get(1).PredictionId == 42, "second input order changed");
        Assert(buffer.Get(2).PredictionId == 43, "third input order changed");

        buffer.Clear();

        Assert(buffer.Count == 0, "clear did not empty the buffer");
        bool threw = false;
        try { buffer.Get(0); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Assert(threw, "reading an empty slot did not fail");
    }

    private static void PredictionSequenceOrdersIdsAcrossWraparound()
    {
        Assert(PredictionSequence.IsNewer(0u, uint.MaxValue), "wrapped id was treated as old");
        Assert(PredictionSequence.IsNewer(uint.MaxValue, uint.MaxValue - 1u), "ordinary next id was treated as old");
        Assert(!PredictionSequence.IsNewer(uint.MaxValue, 0u), "pre-wrap id was treated as new");
        Assert(!PredictionSequence.IsNewer(7u, 7u), "equal ids were treated as ordered");
    }

    private static void ActivationAppliesTagsCostAndCooldown()
    {
        PredictedAbilityRuntime runtime = Runtime();
        FakeResource resource = new FakeResource(50f);
        List<AbilityCueEvent> cues = Capture(runtime);

        bool accepted = runtime.TryActivate(
            Command(AbilityId.ProjectileInteract, 1),
            AbilityExecutionContext.OwnerFirstExecution,
            resource,
            _ => true);

        Assert(accepted, "valid activation was rejected");
        Assert(resource.Current == 40f, "resource cost was not applied exactly once");
        Assert(runtime.HasTag(GameplayTag.CooldownProjectileInteract), "cooldown tag was not granted");
        Assert(cues.Count == 1 && cues[0].Phase == AbilityCuePhase.Predicted, "predicted cue was not emitted once");
        Assert(!runtime.TryActivate(Command(AbilityId.ProjectileInteract, 2), AbilityExecutionContext.OwnerFirstExecution, resource, _ => true),
            "ability ignored its active cooldown");

        runtime.AdvanceTick(GameplayTag.None);
        Assert(runtime.HasTag(GameplayTag.CooldownProjectileInteract), "cooldown expired one step early");
        runtime.AdvanceTick(GameplayTag.None);
        Assert(!runtime.HasTag(GameplayTag.CooldownProjectileInteract), "cooldown did not expire after two steps");
    }

    private static void ExternalBlockedTagRejectsAndCancelsCharge()
    {
        PredictedAbilityRuntime runtime = Runtime();
        FakeResource resource = new FakeResource(50f);
        runtime.AdvanceTick(GameplayTag.StateStunned);
        Assert(!runtime.TryActivate(Command(AbilityId.Charge, 5), AbilityExecutionContext.OwnerFirstExecution, resource, _ => true),
            "stunned actor activated charge");

        runtime.AdvanceTick(GameplayTag.None);
        Assert(runtime.TryActivate(Command(AbilityId.Charge, 6), AbilityExecutionContext.OwnerFirstExecution, resource, _ => true),
            "unstunned actor could not activate charge");
        Assert(runtime.HasTag(GameplayTag.StateCharging), "charge toggle did not grant its tag");

        runtime.AdvanceTick(GameplayTag.StateCarried);
        Assert(!runtime.HasTag(GameplayTag.StateCharging), "carried actor retained charge");
    }

    private static void ReplayRebuildsStateWithoutRepeatingCues()
    {
        PredictedAbilityRuntime runtime = Runtime();
        FakeResource resource = new FakeResource(50f);
        List<AbilityCueEvent> cues = Capture(runtime);
        AbilitySystemSnapshot beforeActivation = runtime.CaptureSnapshot();

        Assert(runtime.TryActivate(Command(AbilityId.Charge, 8), AbilityExecutionContext.OwnerFirstExecution, resource, _ => true),
            "first charge activation failed");
        Assert(cues.Count == 1, "first execution did not emit one cue");

        runtime.Reconcile(beforeActivation);
        cues.Clear();
        Assert(runtime.TryActivate(Command(AbilityId.Charge, 8), AbilityExecutionContext.Replay, resource, _ => true),
            "replay did not rebuild charge state");
        Assert(runtime.HasTag(GameplayTag.StateCharging), "replay failed to restore logical charge state");
        Assert(cues.Count == 0, "replay repeated a cue");
    }

    private static void ReconcileConfirmsAndRejectsSkippedDecisions()
    {
        PredictedAbilityRuntime owner = Runtime();
        PredictedAbilityRuntime server = Runtime();
        FakeResource ownerResource = new FakeResource(50f);
        FakeResource serverResource = new FakeResource(50f);
        List<AbilityCueEvent> cues = Capture(owner);

        for (uint id = 100; id <= 102; id++)
        {
            Assert(owner.TryActivate(Command(AbilityId.ProjectileThrow, id), AbilityExecutionContext.OwnerFirstExecution,
                ownerResource, _ => true), "owner prediction failed");
            bool serverAccepted = server.TryActivate(Command(AbilityId.ProjectileThrow, id), AbilityExecutionContext.Server,
                serverResource, command => command.PredictionId != 101);
            Assert(serverAccepted == (id != 101), "server decision did not match validation");
        }

        cues.Clear();
        owner.Reconcile(server.CaptureSnapshot());

        Assert(cues.Count == 3, "skipped reconcile did not resolve every pending prediction");
        Assert(cues[0].PredictionId == 100 && cues[0].Phase == AbilityCuePhase.Confirmed, "id 100 was not confirmed");
        Assert(cues[1].PredictionId == 101 && cues[1].Phase == AbilityCuePhase.Rejected, "id 101 was not rejected");
        Assert(cues[2].PredictionId == 102 && cues[2].Phase == AbilityCuePhase.Confirmed, "id 102 was not confirmed");
    }

    private static void DecisionHistorySurvivesPredictionIdWraparound()
    {
        AbilityDecisionHistory history = default;
        history.Record(uint.MaxValue, true);
        history.Record(0u, false);
        history.Record(1u, true);

        Assert(history.HighestProcessed == 1u, "decision watermark did not wrap");
        Assert(!history.WasRejected(uint.MaxValue), "accepted pre-wrap id became rejected");
        Assert(history.WasRejected(0u), "rejected wrapped id was lost");
        Assert(!history.WasRejected(1u), "accepted newest id became rejected");
    }

    private static PredictedAbilityRuntime Runtime() => new PredictedAbilityRuntime(new[]
    {
        new AbilitySpec
        {
            Id = AbilityId.Charge,
            ActivationMode = AbilityActivationMode.Toggle,
            BlockedTags = GameplayTag.StateStunned | GameplayTag.StateCarried,
            GrantedTag = GameplayTag.StateCharging
        },
        new AbilitySpec
        {
            Id = AbilityId.ProjectileInteract,
            ActivationMode = AbilityActivationMode.Instant,
            BlockedTags = GameplayTag.StateStunned | GameplayTag.StateCarried,
            CooldownTag = GameplayTag.CooldownProjectileInteract,
            ResourceCost = 10f,
            CooldownSteps = 2
        },
        new AbilitySpec
        {
            Id = AbilityId.ProjectileThrow,
            ActivationMode = AbilityActivationMode.Instant,
            BlockedTags = GameplayTag.StateStunned | GameplayTag.StateCarried
        }
    });

    private static List<AbilityCueEvent> Capture(PredictedAbilityRuntime runtime)
    {
        List<AbilityCueEvent> result = new List<AbilityCueEvent>();
        runtime.Cue += result.Add;
        return result;
    }

    private static AbilityCommand Command(AbilityId ability, uint predictionId) => new AbilityCommand
    {
        Ability = ability,
        Phase = AbilityInputPhase.Pressed,
        PredictionId = predictionId,
        AimDirection = new Vector3(0f, 0f, 1f)
    };

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + exception.Message);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FakeResource : IAbilityResource
    {
        public float Current { get; private set; }

        public FakeResource(float current) => Current = current;

        public bool CanSpend(float amount) => amount >= 0f && Current >= amount;

        public bool TrySpend(float amount)
        {
            if (!CanSpend(amount))
                return false;
            Current -= amount;
            return true;
        }

        public void Refund(float amount) => Current += amount;
    }
}
