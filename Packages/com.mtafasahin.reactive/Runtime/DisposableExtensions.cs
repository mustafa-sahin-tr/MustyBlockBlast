using System;

namespace Mtafasahin.Reactive
{
    public static class DisposableExtensions
    {
        public static IDisposable AddTo(this IDisposable disposable, CompositeDisposable composite)
        {
            if (composite == null)
            {
                throw new ArgumentNullException(nameof(composite));
            }

            composite.Add(disposable);
            return disposable;
        }
    }
}
