using System;
using System.Collections.Generic;

namespace FaceSlapper.Core
{
    /// <summary>订阅者标记接口。</summary>
    public interface ISubscriber { }

    /// <summary>
    /// 通知者：适用于特定组件之间的一对多观察者模式（比 EventBus 更轻、范围更明确）。
    /// </summary>
    public abstract class Notifier
    {
        private readonly List<ISubscriber> _subscribers = new List<ISubscriber>(8);

        public void Attach(ISubscriber subscriber)
        {
            if (subscriber != null && !_subscribers.Contains(subscriber))
                _subscribers.Add(subscriber);
        }

        public void Detach(ISubscriber subscriber) => _subscribers.Remove(subscriber);

        public void ClearSubscribers() => _subscribers.Clear();

        protected void NotifyAll(Action<ISubscriber> notification)
        {
            for (int i = _subscribers.Count - 1; i >= 0; i--)
                notification(_subscribers[i]);
        }
    }

    /// <summary>订阅者基类。</summary>
    public abstract class Subscriber : ISubscriber
    {
        private readonly List<Notifier> _notifiers = new List<Notifier>(4);

        public void SubscribeTo(Notifier notifier)
        {
            if (notifier == null) return;
            notifier.Attach(this);
            if (!_notifiers.Contains(notifier)) _notifiers.Add(notifier);
        }

        public void UnsubscribeFrom(Notifier notifier)
        {
            if (notifier == null) return;
            notifier.Detach(this);
            _notifiers.Remove(notifier);
        }

        /// <summary>取消全部订阅，销毁前调用。</summary>
        public void UnsubscribeAll()
        {
            for (int i = _notifiers.Count - 1; i >= 0; i--)
                _notifiers[i].Detach(this);
            _notifiers.Clear();
        }
    }
}
