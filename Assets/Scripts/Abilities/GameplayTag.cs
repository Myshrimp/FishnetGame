using System;

namespace FaceSlapper.Abilities
{
    [Flags]
    public enum GameplayTag : ulong
    {
        None = 0,
        StateStunned = 1UL << 0,
        StateCarried = 1UL << 1,
        StateCharging = 1UL << 2,
        CooldownProjectileInteract = 1UL << 3,
        CooldownProjectileThrow = 1UL << 4
    }

    public static class GameplayTagExtensions
    {
        public static bool HasAll(this GameplayTag value, GameplayTag required) =>
            (value & required) == required;

        public static bool HasAny(this GameplayTag value, GameplayTag tags) =>
            (value & tags) != 0;
    }
}
