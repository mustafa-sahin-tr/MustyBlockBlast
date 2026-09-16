using System;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Spawns a <see cref="SpecialCellKind.Coin"/> when the combo streak reaches
    /// <see cref="SPAWN_STREAK"/>: one random occupied cell on the board is converted in place.
    /// <para>
    /// Its own System rather than another branch of <see cref="BoardSystem"/>, for the reason
    /// <see cref="LaserSpawnSystem"/> is its own: the trigger is a score concept and
    /// <see cref="BoardSystem"/> deliberately never reads <see cref="ScoreModel"/>. It is shaped
    /// deliberately identically to that System — same seam, same edge-trigger, same selector contract —
    /// because "a streak converts one cell" is one idea with two rewards hanging off it.
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
    /// makes it fire once — see <see cref="LaserSpawnSystem"/> for the full reasoning, which applies
    /// here unchanged: the streak only ever moves by +1 or to 0, so it cannot step over the threshold,
    /// and rebuilding a broken streak to the threshold is a second achievement that has earned a second
    /// coin.
    /// </para>
    /// </summary>
    public sealed class CoinStreakTriggerSystem : IDisposable
    {
        /// <summary>
        /// The streak that earns a coin cell. A placeholder — real balancing is out of this slice's
        /// scope — but deliberately not 4 or 5: those are <see cref="LaserSpawnSystem"/>'s and
        /// <see cref="GoldenPieceTriggerSystem"/>'s thresholds, and sharing one would hand out two
        /// rewards for the same placement, which is a design decision nobody has taken.
        /// </summary>
        private const int SPAWN_STREAK = 6;

        private readonly BoardModel _boardModel;

        /// <summary>Picks which occupied cell is converted. Whether a qualifying streak spawns one at
        /// all is fully deterministic and never touches this.</summary>
        private readonly Random _random;

        private readonly IDisposable _subscription;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public CoinStreakTriggerSystem(ScoreModel scoreModel, BoardModel boardModel)
            : this(scoreModel, boardModel, Environment.TickCount)
        {
        }

        internal CoinStreakTriggerSystem(ScoreModel scoreModel, BoardModel boardModel, int seed)
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

            GridPosition? spawn = CoinSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
            if (spawn == null)
            {
                // No block on the board to convert — a perfect clear that also completed the streak.
                // Silently skipped, which is an ordinary outcome and not an error state.
                return;
            }

            // Converted in place: the block keeps its colour and its occupancy, and the icon is what
            // marks it as special. Nothing is occupied here, so a placement that emptied the board
            // cannot have that achievement quietly taken back by its own reward.
            _boardModel.SetSpecialKind(spawn.Value, SpecialCellKind.Coin);
        }
    }
}
