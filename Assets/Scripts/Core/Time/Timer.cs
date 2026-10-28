using System;

namespace FaceSlapper.Core
{
    /// <summary>
    /// 计时器：由 TimeComponent 统一驱动。支持单次/循环、完成回调、进度回调。
    /// </summary>
    public class Timer
    {
        public float Duration { get; }
        public bool Loop { get; }
        public float Elapsed { get; private set; }
        public bool IsRunning { get; private set; }
        public float Progress => Duration <= 0f ? 1f : Math.Min(Elapsed / Duration, 1f);

        /// <summary>完成时回调（循环模式下每次到点都触发）。</summary>
        public event Action OnComplete;

        /// <summary>每帧回调进度（0~1）。</summary>
        public event Action<float> OnProgress;

        /// <summary>标记为待移除（循环模式下用于手动终止）。</summary>
        public bool Stopped { get; private set; }

        public Timer(float duration, bool loop = false, bool autoStart = true)
        {
            Duration = Math.Max(duration, 0.0001f);
            Loop = loop;
            IsRunning = autoStart;
        }

        public void Start() => IsRunning = true;

        public void Pause() => IsRunning = false;

        public void Reset()
        {
            Elapsed = 0f;
            IsRunning = true;
            Stopped = false;
        }

        public void Stop() => Stopped = true;

        /// <summary>返回 true 表示计时器已完成且应被移除。</summary>
        internal bool Tick(float deltaTime)
        {
            if (!IsRunning || Stopped) return Stopped;

            Elapsed += deltaTime;
            OnProgress?.Invoke(Progress);

            if (Elapsed >= Duration)
            {
                OnComplete?.Invoke();
                if (Loop)
                {
                    Elapsed -= Duration;
                    return false;
                }
                return true;
            }
            return false;
        }
    }
}
