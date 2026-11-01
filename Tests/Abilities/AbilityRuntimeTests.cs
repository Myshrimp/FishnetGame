using System;
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
}
