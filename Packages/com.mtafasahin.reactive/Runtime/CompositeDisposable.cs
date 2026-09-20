using System;
using System.Collections.Generic;

namespace Mtafasahin.Reactive
{
    /// <summary>Holds subscriptions for a View and disposes them all in OnDestroy/OnDisable.</summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> _items = new List<IDisposable>();
        private bool _isDisposed;

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
            {
                return;
            }

            if (_isDisposed)
            {
                disposable.Dispose();
                return;
            }

            _items.Add(disposable);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].Dispose();
            }

            _items.Clear();
        }
    }
}
