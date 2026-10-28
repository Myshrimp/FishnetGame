namespace FaceSlapper.Core
{
    /// <summary>可池化对象接口。实现后由 PoolComponent 在取出/归还时回调。</summary>
    public interface IPoolable
    {
        /// <summary>从池中取出（激活）时调用。</summary>
        void OnGet();

        /// <summary>归还（回池）时调用。</summary>
        void OnReturn();
    }
}
