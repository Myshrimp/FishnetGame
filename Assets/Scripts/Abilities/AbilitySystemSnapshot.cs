using System;

namespace FaceSlapper.Abilities
{
    [Serializable]
    public struct AbilitySlotState
    {
        public AbilityId Id;
        public bool Active;
        public uint CooldownRemainingSteps;
    }

    [Serializable]
    public struct AbilitySystemSnapshot
    {
        public bool Initialized;
        public GameplayTag OwnedTags;
        public AbilitySlotState Slot0;
        public AbilitySlotState Slot1;
        public AbilitySlotState Slot2;
        public AbilitySlotState Slot3;
        public AbilityDecisionHistory Decisions;

        public AbilitySlotState GetSlot(int index)
        {
            switch (index)
            {
                case 0: return Slot0;
                case 1: return Slot1;
                case 2: return Slot2;
                case 3: return Slot3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public void SetSlot(int index, AbilitySlotState value)
        {
            switch (index)
            {
                case 0: Slot0 = value; break;
                case 1: Slot1 = value; break;
                case 2: Slot2 = value; break;
                case 3: Slot3 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
