using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace MustyBlockBlast.Presentation
{
    /// <summary>
    /// Boot System for the splash scene. Waits for a fixed placeholder duration — or until a skip is
    /// requested, whichever comes first — then transitions to the mode-select scene (issue #379),
    /// which in turn hands over to gameplay once a mode is picked. Pure C# — no MonoBehaviour, no
    /// View. Registered as a VContainer entry point.
    /// </summary>
    public sealed class SplashSystem : IAsyncStartable, IDisposable
    {
        private const string TARGET_SCENE_NAME = "ModeSelect";

        private readonly float _splashDurationSeconds;
        private readonly CancellationTokenSource _skipCts = new CancellationTokenSource();

        private bool _hasStarted;
        private bool _transitionStarted;
        private bool _disposed;

        public SplashSystem(float splashDurationSeconds)
        {
            _splashDurationSeconds = splashDurationSeconds;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            if (_hasStarted)
            {
                return;
            }

            _hasStarted = true;

            using (CancellationTokenSource linkedCts =
                   CancellationTokenSource.CreateLinkedTokenSource(cancellation, _skipCts.Token))
            {
                // SuppressCancellationThrow so that a skip simply ends the wait early and falls
                // through into the very same load below — the timeout and the skip share one path.
                await UniTask.Delay(
                        TimeSpan.FromSeconds(_splashDurationSeconds), cancellationToken: linkedCts.Token)
                    .SuppressCancellationThrow();
            }

            // The scene is now committed: any further skip request is a no-op.
            _transitionStarted = true;

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            // SuppressCancellationThrow, because this load destroys the scene that owns this System:
            // the LifetimeScope is disposed and cancels the token as part of the very operation being
            // awaited, so the cancellation is expected teardown, not an error. Without this, every
            // boot logs an OperationCanceledException.
            await SceneManager.LoadSceneAsync(TARGET_SCENE_NAME)
                .ToUniTask(cancellationToken: cancellation)
                .SuppressCancellationThrow();
        }

        /// <summary>
        /// Asks the splash to stop waiting and transition now. Safe to call any number of times, and
        /// a no-op once the transition is already underway.
        /// </summary>
        public void RequestSkip()
        {
            // _disposed is checked too: the scope can be torn down while a press is still in flight,
            // and Cancel() on a disposed source throws.
            if (_disposed || _transitionStarted || _skipCts.IsCancellationRequested)
            {
                return;
            }

            _skipCts.Cancel();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _skipCts.Dispose();
        }
    }
}
