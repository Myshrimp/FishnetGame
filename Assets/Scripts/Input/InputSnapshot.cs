using UnityEngine;

namespace FaceSlapper.Input
{
    /// <summary>一帧的输入快照，由 InputHandler 产出。</summary>
    public struct InputSnapshot
    {
        /// <summary>移动轴（x=左右，y=前后），已归一化。</summary>
        public Vector2 MoveAxis;

        /// <summary>攻击键按下（鼠标左键，按下那一帧为 true）。</summary>
        public bool AttackPressed;

        /// <summary>拾取/放下键按下（E）。</summary>
        public bool PickupPressed;

        /// <summary>击飞技能键按下（Q）。</summary>
        public bool HitbackPressed;

        /// <summary>跳跃键按下（空格，按下那一帧为 true）。</summary>
        public bool JumpPressed;

        /// <summary>加速键按住（Left Shift）。</summary>
        public bool SpeedUpHeld;

        /// <summary>鼠标滚轮增量（相机缩放）。</summary>
        public float ScrollDelta;
    }

    /// <summary>本地输入事件，InputComponent 每帧经 EventBus 派发。</summary>
    public struct LocalInputEvent
    {
        public InputSnapshot Snapshot;
    }
}
