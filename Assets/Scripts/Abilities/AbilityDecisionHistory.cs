using System;

namespace FaceSlapper.Abilities
{
    [Serializable]
    public struct AbilityDecisionHistory
    {
        public bool Initialized;
        public uint HighestProcessed;
        public ulong RejectedBits;

        public void Record(uint predictionId, bool accepted)
        {
            if (!Initialized)
            {
                Initialized = true;
                HighestProcessed = predictionId;
                RejectedBits = accepted ? 0UL : 1UL;
                return;
            }

            if (PredictionSequence.IsNewer(predictionId, HighestProcessed))
            {
                uint distance = predictionId - HighestProcessed;
                RejectedBits = distance >= 64 ? 0UL : RejectedBits << (int)distance;
                HighestProcessed = predictionId;
                if (!accepted)
                    RejectedBits |= 1UL;
                return;
            }

            uint oldDistance = HighestProcessed - predictionId;
            if (!accepted && oldDistance < 64)
                RejectedBits |= 1UL << (int)oldDistance;
        }

        public bool HasProcessed(uint predictionId)
        {
            if (!Initialized)
                return false;
            return predictionId == HighestProcessed || !PredictionSequence.IsNewer(predictionId, HighestProcessed);
        }

        public bool WasRejected(uint predictionId)
        {
            if (!HasProcessed(predictionId))
                return false;

            uint distance = HighestProcessed - predictionId;
            return distance < 64 && (RejectedBits & (1UL << (int)distance)) != 0;
        }
    }
}
