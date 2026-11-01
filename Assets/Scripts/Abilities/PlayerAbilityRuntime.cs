using System;

namespace FaceSlapper.Abilities
{
    public sealed class PlayerAbilityRuntime
    {
        private const float ProjectileInteractionCooldown = 0.15f;
        private readonly PredictedAbilityRuntime _runtime;

        public event Action<AbilityCueEvent> Cue
        {
            add => _runtime.Cue += value;
            remove => _runtime.Cue -= value;
        }

        public PlayerAbilityRuntime(float tickDelta)
        {
            if (tickDelta <= 0f)
                throw new ArgumentOutOfRangeException(nameof(tickDelta));

            uint interactionCooldownSteps = (uint)Math.Ceiling(ProjectileInteractionCooldown / tickDelta);
            GameplayTag controlBlocked = GameplayTag.StateStunned | GameplayTag.StateCarried;
            _runtime = new PredictedAbilityRuntime(new[]
            {
                new AbilitySpec
                {
                    Id = AbilityId.Charge,
                    ActivationMode = AbilityActivationMode.Toggle,
                    BlockedTags = controlBlocked,
                    GrantedTag = GameplayTag.StateCharging
                },
                new AbilitySpec
                {
                    Id = AbilityId.ProjectileInteract,
                    ActivationMode = AbilityActivationMode.Instant,
                    BlockedTags = controlBlocked | GameplayTag.CooldownProjectileInteract,
                    CooldownTag = GameplayTag.CooldownProjectileInteract,
                    CooldownSteps = interactionCooldownSteps
                },
                new AbilitySpec
                {
                    Id = AbilityId.ProjectileThrow,
                    ActivationMode = AbilityActivationMode.Instant,
                    BlockedTags = controlBlocked | GameplayTag.CooldownProjectileInteract,
                    CooldownTag = GameplayTag.CooldownProjectileInteract,
                    CooldownSteps = interactionCooldownSteps
                }
            });
        }

        public bool HasTag(GameplayTag tag) => _runtime.HasTag(tag);

        public void SetStatus(bool stunned, bool carried) =>
            _runtime.SetExternalTags(GetStatusTags(stunned, carried));

        public void AdvanceTick(bool stunned, bool carried) =>
            _runtime.AdvanceTick(GetStatusTags(stunned, carried));

        public bool TryActivate(
            AbilityCommand command,
            AbilityExecutionContext context,
            IAbilityResource resource,
            Func<AbilityCommand, bool> executor) =>
            _runtime.TryActivate(command, context, resource, executor);

        public AbilitySystemSnapshot CaptureSnapshot() => _runtime.CaptureSnapshot();

        public void Reconcile(AbilitySystemSnapshot snapshot) => _runtime.Reconcile(snapshot);

        private static GameplayTag GetStatusTags(bool stunned, bool carried)
        {
            GameplayTag result = GameplayTag.None;
            if (stunned)
                result |= GameplayTag.StateStunned;
            if (carried)
                result |= GameplayTag.StateCarried;
            return result;
        }
    }
}
