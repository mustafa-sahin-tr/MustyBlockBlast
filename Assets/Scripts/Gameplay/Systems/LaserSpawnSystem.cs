using System;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Spawns a <see cref="SpecialCellKind.Laser"/> when the combo streak reaches
    /// <see cref="SPAWN_STREAK"/>: one random occupied cell on the board is converted in place.
    /// <para>
    /// Its own System rather than another branch of <see cref="BoardSystem"/>, because the trigger is a
    /// score concept and <see cref="BoardSystem"/> deliberately never reads <see cref="ScoreModel"/>.
    /// The dependency runs the other way from the explosive core's, whose trigger (a row and a column
    /// closed at once) is something the board itself knows.
    /// </para>
    /// <para>
    /// Driven off <see cref="ScoreModel.Streak"/> directly rather than off
    /// <c>PiecePlacedMessage</c>: the streak is only advanced once <see cref="ScoreSystem"/> has
    /// handled that message, so a second subscriber would be reading a value whose freshness depended
    /// on subscription order. Observing the property instead means this cannot run early, whatever
    /// order the Systems are constructed in.
    /// </para>
    /// <para>
    /// The check is <c>== </c><see cref="SPAWN_STREAK"/>, not <c>&gt;=</c>, and that is exactly what
    /// makes it fire once. The streak only ever moves by +1 (a clearing placement) or to 0 (a
    /// non-clearing one), so it can never step over the threshold, and a run of clears at 5, 6, 7...
    /// re-reaches nothing. <see cref="Gameplay.Reactive.ReactiveProperty{T}"/> raises only on an actual
    /// change, so a value being written again is not an event either. Break the streak and rebuild it
    /// to 4 and the player has earned another laser — which is the intent, not a leak.
    /// </para>
    /// </summary>
    public sealed class LaserSpawnSystem : IDisposable
    {
        /// <summary>The streak that earns a laser.</summary>
        private const int SPAWN_STREAK = 4;

        private readonly BoardModel _boardModel;

        /// <summary>Picks which occupied cell is converted. Whether a qualifying streak spawns one at
        /// all is fully deterministic and never touches this.</summary>
        private readonly Random _random;

        private readonly IDisposable _subscription;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public LaserSpawnSystem(ScoreModel scoreModel, BoardModel boardModel)
            : this(scoreModel, boardModel, Environment.TickCount)
        {
        }

        internal LaserSpawnSystem(ScoreModel scoreModel, BoardModel boardModel, int seed)
        {
            _boardModel = boardModel;
            _random = new Random(seed);

            // Subscribing fires immediately with the current streak, which is 0 at construction and at
            // every run start — never the threshold — so nothing can spawn from merely starting to
            // listen.
            _subscription = scoreModel.Streak.Subscribe(OnStreakChanged);
        }

        public void Dispose() => _subscription.Dispose();

        private void OnStreakChanged(int streak)
        {
            if (streak != SPAWN_STREAK)
            {
                return;
            }

            GridPosition? spawn = LaserSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
            if (spawn == null)
            {
                // No block on the board to convert — a perfect clear that also completed the streak.
                // Silently skipped, which is an ordinary outcome and not an error state.
                return;
            }

            // Converted in place: the block keeps its colour and its occupancy, and the icon is what
            // marks it as special. Nothing is occupied here, so a placement that emptied the board
            // cannot have that achievement quietly taken back by its own reward.
            _boardModel.SetSpecialKind(spawn.Value, SpecialCellKind.Laser);
        }
    }
}
