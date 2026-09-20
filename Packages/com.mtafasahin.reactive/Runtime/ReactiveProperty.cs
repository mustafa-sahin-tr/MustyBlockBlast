using System;
using System.Collections.Generic;

namespace Mtafasahin.Reactive
{
    /// <summary>
    /// Minimal observable value. The project has no R3/UniRx dependency, so this is the
    /// project-local stand-in used by Models so Views can subscribe instead of polling.
    /// Writes are <c>internal</c>: only Systems (same assembly) mutate model state.
    /// </summary>
    public sealed class ReactiveProperty<T>
    {
        private static readonly EqualityComparer<T> Comparer = EqualityComparer<T>.Default;

        private readonly List<Action<T>> _handlers = new List<Action<T>>();
        private T _value;

        public ReactiveProperty()
        {
            _value = default;
        }

        public ReactiveProperty(T initialValue)
        {
            _value = initialValue;
        }

        public T Value
        {
            get => _value;
            internal set
            {
                if (Comparer.Equals(_value, value))
                {
                    return;
                }

                _value = value;
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    _handlers[i].Invoke(value);
                }
            }
        }

        /// <summary>Subscribes and immediately invokes the handler with the current value.</summary>
        public IDisposable Subscribe(Action<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _handlers.Add(handler);
            handler.Invoke(_value);
            return new Subscription(this, handler);
        }

        private sealed class Subscription : IDisposable
        {
            private ReactiveProperty<T> _owner;
            private Action<T> _handler;

            internal Subscription(ReactiveProperty<T> owner, Action<T> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner._handlers.Remove(_handler);
                _owner = null;
                _handler = null;
            }
        }
    }
}
