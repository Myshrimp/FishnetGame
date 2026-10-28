using UnityEngine;

namespace FaceSlapper.Network.Prediction
{
    /// <summary>
    /// Runtime combat status which affects prediction. The owning motor reconciles all fields.
    /// </summary>
    public sealed class PlayerStatusController
    {
        public uint StunEndTick { get; private set; }
        public Vector3 KnockbackVelocity { get; private set; }

        public bool IsStunned(uint tick) => tick < StunEndTick;

        public void ApplyStun(uint startTick, uint durationTicks)
        {
            if (durationTicks == 0)
                return;

            uint endTick = startTick + durationTicks;
            if (endTick > StunEndTick)
                StunEndTick = endTick;
        }

        public void ApplyKnockback(Vector3 velocity)
        {
            // Vertical motion is handled by the motor's gravity velocity.
            velocity.y = 0f;
            KnockbackVelocity = velocity;
        }

        public void Simulate(float delta, float damping)
        {
            KnockbackVelocity = Vector3.MoveTowards(
                KnockbackVelocity,
                Vector3.zero,
                damping * delta);
        }

        public void Reconcile(uint stunEndTick, Vector3 knockbackVelocity)
        {
            StunEndTick = stunEndTick;
            KnockbackVelocity = knockbackVelocity;
        }
    }
}
