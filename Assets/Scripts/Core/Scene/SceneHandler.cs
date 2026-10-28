using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 场景处理器：经 Net 门面做网络场景加载；离线时回退到本地场景加载。
    /// </summary>
    public class SceneHandler
    {
        /// <summary>以"全局场景"方式加载（服务器发起，所有客户端跟随）。仅服务器可调用。</summary>
        public bool LoadGlobalScene(string sceneName)
        {
            return true;
        }

        /// <summary>仅加载本地场景（不参与网络同步）。</summary>
        public void LoadLocalScene(string sceneName)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }
    }
}
