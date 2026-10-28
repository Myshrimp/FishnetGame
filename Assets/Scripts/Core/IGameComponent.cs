namespace FaceSlapper.Core
{
    /// <summary>
    /// 游戏组件接口。所有挂载到 GameManager 上的功能组件都实现该接口，
    /// 由 GameEntry 统一注册、GameManager 统一驱动生命周期。
    /// </summary>
    public interface IGameComponent
    {
        /// <summary>组件被注册到 GameManager 时调用。</summary>
        void OnInit();

        /// <summary>组件被反注册 / 游戏退出时调用。</summary>
        void OnShutdown();
    }

    /// <summary>需要每帧更新的组件实现该接口。</summary>
    public interface IUpdatable
    {
        void OnUpdate(float deltaTime);
    }

    /// <summary>需要固定帧率更新的组件实现该接口。</summary>
    public interface IFixedUpdatable
    {
        void OnFixedUpdate(float deltaTime);
    }

    /// <summary>需要在 LateUpdate 更新的组件实现该接口。</summary>
    public interface ILateUpdatable
    {
        void OnLateUpdate(float deltaTime);
    }
}
