using System;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;
using VContainer.Unity;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="DoubleMultiplierModel"/>: the countdown of the "2x frenzy" window opened by
    /// <see cref="PowerUpKind.DoubleMultiplier"/>. <see cref="PowerUpSystem"/> spends the power-up and
    /// calls <see cref="Activate"/>; this class does nothing but run the clock down and close the
    /// window, so the doubling itself stays a pure read of the model in the two scoring systems.
    /// <para>
    /// It is an <see cref="ITickable"/> rather than a reactive sampler of wall-clock time — the shape
    /// <c>ObjectiveSystem</c>'s rolling windows use — because this window must expire on its own: a
    /// player who activates it and then does nothing for 15 seconds has to come out the far side with
    /// the doubling off, and there is no placement to sample the clock on. That also makes it the
    /// <c>TimerRunSystem</c> shape, which is the established one for a countdown here.
    /// </para>
    /// <para>
    /// Pausing is read straight off <see cref="RunPauseModel.IsPaused"/> — the same three reasons the
    /// timed-mode countdown already honours (app backgrounded, a modal panel open, a power-up armed and
    /// being aimed). Because the window is driven by accumulated delta rather than by timestamps, a
    /// paused frame is simply a frame that is not counted; there is no paused span to subtract
    /// afterwards and so no way for the two to drift.
    /// </para>
    /// </summary>
    public sealed class DoubleMultiplierSystem : ITickable, IDisposable
    {
        private readonly DoubleMultiplierModel _doubleMultiplierModel;
        private readonly RunPauseModel _runPauseModel;
        private readonly IDisposable _subscriptions;

        public DoubleMultiplierSystem(
            DoubleMultiplierModel doubleMultiplierModel,
            RunPauseModel runPauseModel,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _doubleMultiplierModel = doubleMultiplierModel;
            _runPauseModel = runPauseModel;

            // A window belongs to the run it was activated in, exactly as an armed selection does: it
            // must not survive either run boundary, or the next run would open already doubling.
            var bag = DisposableBag.CreateBuilder();
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        void ITickable.Tick() => Advance(Time.deltaTime);

        /// <summary>
        /// Opens a full-length window, or restarts one already running back to full length. Restarting
        /// rather than stacking is deliberate: two windows at once would have to mean 4x, and the
        /// power-up is defined as a doubling, not as a stackable multiplier.
        /// </summary>
        internal void Activate()
        {
            _doubleMultiplierModel.RemainingSeconds.Value = DoubleMultiplierModel.WINDOW_SECONDS;
        }

        /// <summary>
        /// Burns <paramref name="deltaSeconds"/> of the open window, closing it once it runs out. A
        /// paused run burns nothing, so the seconds spent in a menu, aiming another power-up or with
        /// the app backgrounded are not charged against the frenzy.
        /// <para>
        /// Separate from <see cref="ITickable.Tick"/> — which supplies nothing but
        /// <see cref="Time.deltaTime"/> — so the countdown can be driven a known number of seconds at a
        /// time in a test without a frame or a wall clock.
        /// </para>
        /// </summary>
        internal void Advance(float deltaSeconds)
        {
            if (!_doubleMultiplierModel.IsActive || _runPauseModel.IsPaused.Value)
            {
                return;
            }

            float remaining = _doubleMultiplierModel.RemainingSeconds.Value - deltaSeconds;
            _doubleMultiplierModel.RemainingSeconds.Value = remaining > 0f ? remaining : 0f;
        }

        private void OnRunStarted(RunStartedMessage message) => Deactivate();

        private void OnGameOver(GameOverMessage message) => Deactivate();

        private void Deactivate() => _doubleMultiplierModel.RemainingSeconds.Value = 0f;
    }
}
