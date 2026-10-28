using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>场景管理组件：持有 SceneHandler，供其他系统经 GameManager 访问。</summary>
    public class SceneManagementComponent : MonoBehaviour, IGameComponent
    {
        public SceneHandler Handler { get; private set; }

        public void OnInit() => Handler = new SceneHandler();

        public void OnShutdown() => Handler = null;

        /// <summary>网络全局场景加载（服务器）或本地加载（离线回退）。</summary>
        public bool LoadScene(string sceneName) => Handler.LoadGlobalScene(sceneName);
    }
}
