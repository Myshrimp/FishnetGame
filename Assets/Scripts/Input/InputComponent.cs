using FaceSlapper.Core;
using UnityEngine;

namespace FaceSlapper.Input
{
    /// <summary>
    /// 输入组件：内置 InputHandler，每帧轮询产出 InputSnapshot，
    /// 供本地玩家脚本直接读取（Current），同时经 EventBus 派发 LocalInputEvent。
    /// GM 命令行打开期间输出空快照，避免误操作。
    /// </summary>
    public class InputComponent : MonoBehaviour, IGameComponent, IUpdatable
    {
        public InputHandler Handler { get; } = new InputHandler();

        /// <summary>本帧输入快照。</summary>
        public InputSnapshot Current { get; private set; }

        public InputSnapshot OverrideSnap;
        public bool IsOverride;

        public void OnInit() { }

        public void OnShutdown() { }

        public void OnUpdate(float deltaTime)
        {
            if(IsOverride)
            {
                Current = OverrideSnap;
            }
            EventBus.Publish(new LocalInputEvent { Snapshot = Current });
        }
    }
}
