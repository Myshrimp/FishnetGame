using System;

namespace FaceSlapper.Abilities
{
    public interface IAbilityResource
    {
        float Current { get; }
        bool CanSpend(float amount);
        bool TrySpend(float amount);
        void Refund(float amount);
    }

    public readonly struct AbilityExecutionContext
    {
        internal readonly bool TrackPrediction;
        internal readonly bool RecordDecision;

        private AbilityExecutionContext(bool trackPrediction, bool recordDecision)
        {
            TrackPrediction = trackPrediction;
            RecordDecision = recordDecision;
        }

        public static AbilityExecutionContext OwnerFirstExecution => new AbilityExecutionContext(true, false);
        public static AbilityExecutionContext Replay => new AbilityExecutionContext(false, false);
        public static AbilityExecutionContext Server => new AbilityExecutionContext(false, true);
        public static AbilityExecutionContext Spectator => new AbilityExecutionContext(false, false);
    }

    public sealed class PredictedAbilityRuntime
    {
        private const int MaximumSlots = 4;
        private const int MaximumPendingPredictions = 32;

        private struct PendingPrediction
        {
            public AbilityId Ability;
            public uint PredictionId;
        }

        private readonly AbilitySpec[] _specs;
        private readonly PendingPrediction[] _pending = new PendingPrediction[MaximumPendingPredictions];
        private AbilitySystemSnapshot _state;
        private GameplayTag _externalTags;
        private int _pendingStart;
        private int _pendingCount;

        public event Action<AbilityCueEvent> Cue;

        public GameplayTag Tags => _state.OwnedTags | _externalTags;
        public AbilityDecisionHistory Decisions => _state.Decisions;

        public PredictedAbilityRuntime(AbilitySpec[] specs)
        {
            if (specs == null)
                throw new ArgumentNullException(nameof(specs));
            if (specs.Length > MaximumSlots)
                throw new ArgumentException($"A maximum of {MaximumSlots} ability specs is supported.", nameof(specs));

            _specs = new AbilitySpec[specs.Length];
            Array.Copy(specs, _specs, specs.Length);
            _state.Initialized = true;
            for (int i = 0; i < specs.Length; i++)
                _state.SetSlot(i, new AbilitySlotState { Id = specs[i].Id });
        }

        public bool HasTag(GameplayTag tag) => Tags.HasAll(tag);

        public void SetExternalTags(GameplayTag tags)
        {
            _externalTags = tags;
            CancelBlockedActiveAbilities();
        }

        public void AdvanceTick(GameplayTag externalTags)
        {
            SetExternalTags(externalTags);
            for (int i = 0; i < _specs.Length; i++)
            {
                AbilitySlotState slot = _state.GetSlot(i);
                if (slot.CooldownRemainingSteps == 0)
                    continue;

                slot.CooldownRemainingSteps--;
                if (slot.CooldownRemainingSteps == 0)
                    _state.OwnedTags &= ~_specs[i].CooldownTag;
                _state.SetSlot(i, slot);
            }
        }

        public bool TryActivate(
            AbilityCommand command,
            AbilityExecutionContext context,
            IAbilityResource resource,
            Func<AbilityCommand, bool> executor)
        {
            int index = FindSpec(command.Ability);
            if (index < 0 || resource == null || executor == null)
                return RecordResult(command, context, false);

            AbilitySpec spec = _specs[index];
            AbilitySlotState slot = _state.GetSlot(index);
            GameplayTag tags = Tags;
            bool valid = tags.HasAll(spec.RequiredTags)
                && !tags.HasAny(spec.BlockedTags)
                && slot.CooldownRemainingSteps == 0
                && resource.CanSpend(spec.ResourceCost);
            if (!valid)
                return RecordResult(command, context, false);

            if (!resource.TrySpend(spec.ResourceCost))
                return RecordResult(command, context, false);

            if (!executor(command))
            {
                resource.Refund(spec.ResourceCost);
                return RecordResult(command, context, false);
            }

            ApplyActivation(index, spec, slot);
            RecordResult(command, context, true);
            if (context.TrackPrediction)
            {
                AddPending(command);
                Cue?.Invoke(new AbilityCueEvent(command.Ability, command.PredictionId, AbilityCuePhase.Predicted));
            }
            return true;
        }

        public AbilitySystemSnapshot CaptureSnapshot() => _state;

        public void Reconcile(AbilitySystemSnapshot snapshot)
        {
            if (!snapshot.Initialized)
                return;

            ResolvePending(snapshot.Decisions);
            _state = snapshot;
            CancelBlockedActiveAbilities();
        }

        private void ApplyActivation(int index, AbilitySpec spec, AbilitySlotState slot)
        {
            if (spec.ActivationMode == AbilityActivationMode.Toggle)
            {
                slot.Active = !slot.Active;
                if (slot.Active)
                    _state.OwnedTags |= spec.GrantedTag;
                else
                    _state.OwnedTags &= ~spec.GrantedTag;
            }
            else if (spec.GrantedTag != GameplayTag.None)
            {
                _state.OwnedTags |= spec.GrantedTag;
            }

            slot.CooldownRemainingSteps = spec.CooldownSteps;
            if (slot.CooldownRemainingSteps > 0)
                _state.OwnedTags |= spec.CooldownTag;
            _state.SetSlot(index, slot);
        }

        private bool RecordResult(AbilityCommand command, AbilityExecutionContext context, bool accepted)
        {
            if (context.RecordDecision)
            {
                _state.Decisions.Record(command.PredictionId, accepted);
            }
            return accepted;
        }

        private void CancelBlockedActiveAbilities()
        {
            GameplayTag tags = Tags;
            for (int i = 0; i < _specs.Length; i++)
            {
                AbilitySlotState slot = _state.GetSlot(i);
                AbilitySpec spec = _specs[i];
                if (!slot.Active || !tags.HasAny(spec.BlockedTags))
                    continue;

                slot.Active = false;
                _state.OwnedTags &= ~spec.GrantedTag;
                _state.SetSlot(i, slot);
            }
        }

        private int FindSpec(AbilityId id)
        {
            for (int i = 0; i < _specs.Length; i++)
            {
                if (_specs[i].Id == id)
                    return i;
            }
            return -1;
        }

        private void AddPending(AbilityCommand command)
        {
            if (_pendingCount == MaximumPendingPredictions)
            {
                _pendingStart = (_pendingStart + 1) % MaximumPendingPredictions;
                _pendingCount--;
            }

            int index = (_pendingStart + _pendingCount) % MaximumPendingPredictions;
            _pending[index] = new PendingPrediction
            {
                Ability = command.Ability,
                PredictionId = command.PredictionId
            };
            _pendingCount++;
        }

        private void ResolvePending(AbilityDecisionHistory decisions)
        {
            while (_pendingCount > 0)
            {
                PendingPrediction pending = _pending[_pendingStart];
                if (!decisions.HasProcessed(pending.PredictionId))
                    break;

                AbilityCuePhase phase = decisions.WasRejected(pending.PredictionId)
                    ? AbilityCuePhase.Rejected
                    : AbilityCuePhase.Confirmed;
                Cue?.Invoke(new AbilityCueEvent(pending.Ability, pending.PredictionId, phase));
                _pendingStart = (_pendingStart + 1) % MaximumPendingPredictions;
                _pendingCount--;
            }
        }
    }
}
