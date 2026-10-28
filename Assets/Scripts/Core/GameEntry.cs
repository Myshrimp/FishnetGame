using FaceSlapper.Input;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 游戏引导器：场景加载时创建 GameManager 并按顺序注册所有游戏组件。
    /// 场景中只需放一个挂了 GameEntry 的物体即可。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameEntry : MonoBehaviour
    {
        private void Awake()
        {
            GameManager gm = GameManager.Instance;

            // 基础组件优先，业务组件在后。
            gm.AddAndRegister<LogComponent>();
            gm.AddAndRegister<TimeComponent>();
            gm.AddAndRegister<PoolComponent>();
            gm.AddAndRegister<InputComponent>();
            gm.AddAndRegister<SceneManagementComponent>();
        }
    }
}
