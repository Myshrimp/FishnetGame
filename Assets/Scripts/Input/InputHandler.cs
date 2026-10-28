using UnityEngine;

namespace FaceSlapper.Input
{
    /// <summary>
    /// 输入处理器：纯 C# 类，集中轮询所有用户输入并产出 InputSnapshot。
    /// 直接用 KeyCode 轮询，不依赖 InputManager.asset 的轴配置。
    /// 按键可配置，便于后续接键位绑定界面。
    /// </summary>
    public class InputHandler
    {
        // 移动
        public KeyCode MoveUp = KeyCode.W;
        public KeyCode MoveDown = KeyCode.S;
        public KeyCode MoveLeft = KeyCode.A;
        public KeyCode MoveRight = KeyCode.D;

        // 功能键
        public KeyCode Pickup = KeyCode.E;
        public KeyCode Hitback = KeyCode.Q;
        public KeyCode SpeedUp = KeyCode.LeftShift;
        public KeyCode Jump = KeyCode.Space;

        // 鼠标
        public int AttackButton = 0;

        public InputSnapshot Poll()
        {
            var snapshot = new InputSnapshot();

            float h = (IsDown(MoveRight) ? 1f : 0f) - (IsDown(MoveLeft) ? 1f : 0f);
            float v = (IsDown(MoveUp) ? 1f : 0f) - (IsDown(MoveDown) ? 1f : 0f);
            // 方向键作为备用。
            h += (IsDown(KeyCode.RightArrow) ? 1f : 0f) - (IsDown(KeyCode.LeftArrow) ? 1f : 0f);
            v += (IsDown(KeyCode.UpArrow) ? 1f : 0f) - (IsDown(KeyCode.DownArrow) ? 1f : 0f);
            snapshot.MoveAxis = Vector2.ClampMagnitude(new Vector2(h, v), 1f);

            snapshot.AttackPressed = UnityEngine.Input.GetMouseButtonDown(AttackButton);
            snapshot.PickupPressed = IsDownOnce(Pickup);
            snapshot.HitbackPressed = IsDownOnce(Hitback);
            snapshot.JumpPressed = IsDownOnce(Jump);
            snapshot.SpeedUpHeld = IsDown(SpeedUp) || IsDown(KeyCode.RightShift);
            snapshot.ScrollDelta = UnityEngine.Input.mouseScrollDelta.y;

            return snapshot;
        }

        private static bool IsDown(KeyCode key) => UnityEngine.Input.GetKey(key);

        private static bool IsDownOnce(KeyCode key) => UnityEngine.Input.GetKeyDown(key);
    }
}
