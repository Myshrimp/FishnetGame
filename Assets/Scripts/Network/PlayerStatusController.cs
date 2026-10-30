using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    /// <summary>
    /// Runtime combat status which affects prediction. The owning motor reconciles all fields.
    /// </summary>
    public sealed class PlayerStatusController
    {
        public uint StunRemainingSteps { get; private set; }
        public Vector3 KnockbackVelocity { get; private set; }

        public bool IsStunned => StunRemainingSteps > 0;

        public void ApplyStun(uint durationSteps)
        {
            if (durationSteps > StunRemainingSteps)
                StunRemainingSteps = durationSteps;
        }

        internal void AdvanceStun()
        {
            if (StunRemainingSteps > 0)
                StunRemainingSteps--;
        }

        public void ApplyKnockback(Vector3 velocity)
        {
            // Vertical motion is handled by the motor's gravity velocity.
            velocity.y = 0f;
            KnockbackVelocity = velocity;
        }

        public void Simulate(float delta, float damping)
        {
            AdvanceStun();
            KnockbackVelocity = Vector3.MoveTowards(
                KnockbackVelocity,
                Vector3.zero,
                damping * delta);
        }

        public void Reconcile(uint stunRemainingSteps, Vector3 knockbackVelocity)
        {
            StunRemainingSteps = stunRemainingSteps;
            KnockbackVelocity = knockbackVelocity;
        }
    }
}
