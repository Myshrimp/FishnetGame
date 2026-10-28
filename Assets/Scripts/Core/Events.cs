using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>本地玩家生成事件（相机、输入绑定等监听）。</summary>
    public struct LocalPlayerSpawnedEvent
    {
    }

    /// <summary>本地玩家被销毁事件。</summary>
    public struct LocalPlayerDespawnedEvent { }

    /// <summary>玩家被击中事件（表现层可监听）。</summary>
    public struct PlayerHitEvent
    {
        public int VictimNetId;
        public Vector3 Direction;
        public float Force;
    }
}
