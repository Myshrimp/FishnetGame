using System;

namespace FaceSlapper.Bit
{
    public static class Bit
    {
        public static int SetBit(this int value, int bitIndex)
        {
            return value | GetMask(bitIndex);
        }

        public static int ClearBit(this int value, int bitIndex)
        {
            return value & ~GetMask(bitIndex);
        }

        public static int ToggleBit(this int value, int bitIndex)
        {
            return value ^ GetMask(bitIndex);
        }

        public static bool IsBitSet(this int value, int bitIndex)
        {
            return (value & GetMask(bitIndex)) != 0;
        }

        private static int GetMask(int bitIndex)
        {
            if (bitIndex < 0 || bitIndex > 31)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bitIndex), bitIndex, "位索引必须在 0～31 之间。");
            }

            return 1 << bitIndex;
        }
    }
}