using System;
using System.Collections.Generic;
using UnityEngine;

namespace FaceSlapper.Core
{
    /// <summary>计时器系统组件：统一驱动所有 Timer。</summary>
    public class TimeComponent : MonoBehaviour, IGameComponent, IUpdatable
    {
        private readonly List<Timer> _timers = new List<Timer>(32);

        public void OnInit() { }

        public void OnShutdown() => _timers.Clear();

        /// <summary>创建一个计时器。</summary>
        /// <param name="duration">时长（秒）。</param>
        /// <param name="onComplete">完成回调。</param>
        /// <param name="loop">是否循环。</param>
        public Timer CreateTimer(float duration, Action onComplete = null, bool loop = false)
        {
            var timer = new Timer(duration, loop);
            if (onComplete != null) timer.OnComplete += onComplete;
            _timers.Add(timer);
            return timer;
        }

        public void RemoveTimer(Timer timer)
        {
            if (timer != null) _timers.Remove(timer);
        }

        public void OnUpdate(float deltaTime)
        {
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                if (_timers[i].Tick(deltaTime))
                    _timers.RemoveAt(i);
            }
        }
    }
}
