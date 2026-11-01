using System;
using UnityEngine;

namespace FaceSlapper.Abilities
{
    public enum AbilityId : byte
    {
        None = 0,
        Charge = 1,
        ProjectileInteract = 2,
        ProjectileThrow = 3
    }

    public enum AbilityActivationMode : byte
    {
        Instant = 0,
        Toggle = 1
    }

    public enum AbilityInputPhase : byte
    {
        Pressed = 0,
        Held = 1,
        Released = 2
    }

    public enum AbilityCuePhase : byte
    {
        Predicted = 0,
        Confirmed = 1,
        Rejected = 2,
        Activated = 3,
        Ended = 4
    }

    [Serializable]
    public struct AbilityCommand
    {
        public AbilityId Ability;
        public AbilityInputPhase Phase;
        public uint PredictionId;
        public Vector3 AimDirection;
    }

    [Serializable]
    public struct AbilityCommandBuffer
    {
        public byte Count;
        public AbilityCommand Command0;
        public AbilityCommand Command1;
        public AbilityCommand Command2;

        public bool TryAdd(AbilityCommand value)
        {
            switch (Count)
            {
                case 0:
                    Command0 = value;
                    break;
                case 1:
                    Command1 = value;
                    break;
                case 2:
                    Command2 = value;
                    break;
                default:
                    return false;
            }

            Count++;
            return true;
        }

        public AbilityCommand Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            switch (index)
            {
                case 0: return Command0;
                case 1: return Command1;
                default: return Command2;
            }
        }

        public void Clear()
        {
            Count = 0;
            Command0 = default;
            Command1 = default;
            Command2 = default;
        }
    }

    [Serializable]
    public struct AbilitySpec
    {
        public AbilityId Id;
        public AbilityActivationMode ActivationMode;
        public GameplayTag RequiredTags;
        public GameplayTag BlockedTags;
        public GameplayTag GrantedTag;
        public GameplayTag CooldownTag;
        public float ResourceCost;
        public uint CooldownSteps;
    }

    public readonly struct AbilityCueEvent
    {
        public readonly AbilityId Ability;
        public readonly uint PredictionId;
        public readonly AbilityCuePhase Phase;

        public AbilityCueEvent(AbilityId ability, uint predictionId, AbilityCuePhase phase)
        {
            Ability = ability;
            PredictionId = predictionId;
            Phase = phase;
        }
    }

    public static class PredictionSequence
    {
        public static bool IsNewer(uint candidate, uint baseline) =>
            candidate != baseline && unchecked((int)(candidate - baseline)) > 0;

        public static bool IsNewerOrEqual(uint candidate, uint baseline) =>
            candidate == baseline || IsNewer(candidate, baseline);
    }
}
