using System;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Arms a <see cref="SpecialPieceKind.Golden"/> dock injection when the combo streak reaches
    /// <see cref="TRIGGER_STREAK"/>. The next refill hands the player the golden 1x1 in place of one
    /// normally drawn piece.
    /// <para>
    /// Its own System rather than another branch of <see cref="BoardSystem"/>, for the reason
    /// <see cref="LaserSpawnSystem"/> is one: the trigger is a score concept and
    /// <see cref="BoardSystem"/> deliberately never reads <see cref="ScoreModel"/>. Where the laser
    /// converts a board cell on the spot, this one can only ever make a <em>request</em> — the dock is
    /// <see cref="BoardSystem"/>'s to write, and the injection has to wait for a refill — so the
    /// dependency is a single call rather than a model mutation.
    /// </para>
    /// <para>
    /// Driven off <see cref="ScoreModel.Streak"/> directly rather than off <c>PiecePlacedMessage</c>,
    /// exactly as <see cref="LaserSpawnSystem"/> is: the streak is only advanced once
    /// <see cref="ScoreSystem"/> has handled that message, so a second subscriber would be reading a
    /// value whose freshness depended on subscription order. Observing the property instead means this
    /// cannot run early, whatever order the Systems are constructed in.
    /// </para>
    /// <para>
    /// The check is <c>== </c><see cref="TRIGGER_STREAK"/>, not <c>&gt;=</c>, and that is exactly what
    /// makes it fire once. The streak only ever moves by +1 (a clearing placement) or to 0 (a
    /// non-clearing one), so it can never step over the threshold, and a run of clears at 5, 6, 7...
    /// re-reaches nothing. <see cref="Gameplay.Reactive.ReactiveProperty{T}"/> raises only on an actual
    /// change, so a value being written again is not an event either. Break the streak and rebuild it to
    /// 5 and the player has earned another golden piece — which is the intent, not a leak.
    /// </para>
    /// </summary>
    public sealed class GoldenPieceTriggerSystem : IDisposable
    {
        /// <summary>The streak that earns a golden 1x1.</summary>
        private const int TRIGGER_STREAK = 5;

        private readonly BoardSystem _boardSystem;
        private readonly IDisposable _subscription;

        [Inject]
        public GoldenPieceTriggerSystem(ScoreModel scoreModel, BoardSystem boardSystem)
        {
            _boardSystem = boardSystem;

            // Subscribing fires immediately with the current streak, which is 0 at construction and at
            // every run start — never the threshold — so nothing can be armed by merely starting to
            // listen.
            _subscription = scoreModel.Streak.Subscribe(OnStreakChanged);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnStreakChanged(int streak)
        {
            if (streak != TRIGGER_STREAK)
            {
                return;
            }

            _boardSystem.RequestGoldenPieceInjection();
        }
    }
}
