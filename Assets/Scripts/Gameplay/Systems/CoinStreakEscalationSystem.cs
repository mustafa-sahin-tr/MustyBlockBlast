using System;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Drops a <see cref="SpecialCellKind.Coin"/> cell onto the board at every combo streak level from
    /// <see cref="FIRST_PAYING_STREAK"/> to <see cref="LAST_PAYING_STREAK"/>, each worth twice the one
    /// before it: 1, 2, 4, 8, 16 coins for streak 2, 3, 4, 5, 6 (issue #401). One random occupied cell
    /// is converted in place per level reached, and priced then and there
    /// (<see cref="BoardModel.SetCoinCell"/>) so the coin carries its own value to whatever destroys it.
    /// <para>
    /// Replaces the old <c>CoinStreakTriggerSystem</c>, which paid one fixed-value coin at streak 6 and
    /// nothing else. Same seam, same subscription, same selector contract — it is
    /// <see cref="LaserSpawnSystem"/>'s shape with an escalating schedule where the single threshold
    /// used to be.
    /// </para>
    /// <para>
    /// Its own System rather than another branch of <see cref="BoardSystem"/>, for the reason
    /// <see cref="LaserSpawnSystem"/> is its own: the trigger is a score concept and
    /// <see cref="BoardSystem"/> deliberately never reads <see cref="ScoreModel"/>. Driven off
    /// <see cref="ScoreModel.Streak"/> directly rather than off <c>PiecePlacedMessage</c> for the reason
    /// that System documents: the streak is only advanced once <see cref="ScoreSystem"/> has handled
    /// that message, and observing the property means this cannot run early whatever order the Systems
    /// are constructed in.
    /// </para>
    /// <para>
    /// <b>How the escalation resets.</b> The System remembers the highest streak level it has already
    /// paid in the current run of clears (<see cref="_highestPaidStreak"/>). A streak change to a value
    /// below <see cref="FIRST_PAYING_STREAK"/> — which in practice is always the 0 a non-clearing
    /// placement writes — forgets it, so the next run of clears starts again at streak 2 = 1 coin
    /// rather than continuing from wherever the last one left off (AC4/AC9). A streak change that
    /// merely climbs past <see cref="LAST_PAYING_STREAK"/> forgets nothing and pays nothing: the
    /// schedule is capped, not repeating (AC3).
    /// </para>
    /// <para>
    /// Each level pays at most once per run of clears. <see cref="ScoreSystem"/> only ever moves the
    /// streak by +1 or to 0, so a level is "reached" exactly once on the way up and this bookkeeping is
    /// belt-and-braces there; where it earns its keep is in stating the rule precisely enough that a
    /// future streak that could jump (a multi-clear counting for more, say) would pay every level it
    /// stepped over exactly once, in order, rather than double-paying or skipping one.
    /// </para>
    /// </summary>
    public sealed class CoinStreakEscalationSystem : IDisposable
    {
        /// <summary>The first streak level that drops a coin, worth 1 coin. Below it nothing pays.</summary>
        internal const int FIRST_PAYING_STREAK = 2;

        /// <summary>The last streak level that drops a coin, worth 16. Above it nothing further pays.</summary>
        internal const int LAST_PAYING_STREAK = 6;

        private readonly BoardModel _boardModel;

        /// <summary>Picks which occupied cell is converted. Whether a qualifying streak drops a coin at
        /// all, and what it is worth, is fully deterministic and never touches this.</summary>
        private readonly Random _random;

        private readonly IDisposable _subscription;

        /// <summary>Optional: null in test constructions. Guarded on publish so an un-injected instance
        /// behaves exactly as an injected one does on the board.</summary>
        private readonly IPublisher<SpecialCellSpawnedMessage> _specialCellSpawnedPublisher;

        /// <summary>
        /// Which rule set the current run is played under. Read only by <see cref="OnStreakChanged"/>
        /// so Classic mode's run (<see cref="GameMode.Timed"/> — issue #355) never earns a coin cell,
        /// exactly as it never earns any other special cell — see <see cref="LaserSpawnSystem"/>'s own
        /// field for the full reasoning, which applies here unchanged. Nullable, treated as "extras
        /// enabled" when null.
        /// </summary>
        private readonly GameModeModel _gameModeModel;

        /// <summary>The highest streak level already paid in the current run of clears; 0 when nothing
        /// has been paid since the streak last broke. The one piece of state the escalation needs.</summary>
        private int _highestPaidStreak;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public CoinStreakEscalationSystem(
            ScoreModel scoreModel,
            BoardModel boardModel,
            IPublisher<SpecialCellSpawnedMessage> specialCellSpawnedPublisher = null,
            GameModeModel gameModeModel = null)
            : this(scoreModel, boardModel, Environment.TickCount, specialCellSpawnedPublisher, gameModeModel)
        {
        }

        internal CoinStreakEscalationSystem(
            ScoreModel scoreModel,
            BoardModel boardModel,
            int seed,
            IPublisher<SpecialCellSpawnedMessage> specialCellSpawnedPublisher = null,
            GameModeModel gameModeModel = null)
        {
            _boardModel = boardModel;
            _random = new Random(seed);
            _specialCellSpawnedPublisher = specialCellSpawnedPublisher;
            _gameModeModel = gameModeModel;

            // Subscribing fires immediately with the current streak, which is 0 at construction and at
            // every run start — below the first paying level — so nothing can drop from merely starting
            // to listen.
            _subscription = scoreModel.Streak.Subscribe(OnStreakChanged);
        }

        public void Dispose() => _subscription.Dispose();

        /// <summary>Coins the drop at <paramref name="streak"/> is worth: 2^(streak - 2), so 1 at the
        /// first paying level and doubling at each after it. Only meaningful within the paying range.</summary>
        internal static int CoinValueForStreak(int streak) => 1 << (streak - FIRST_PAYING_STREAK);

        private void OnStreakChanged(int streak)
        {
            if (streak < FIRST_PAYING_STREAK)
            {
                // The streak broke (or has not started paying yet). Forget the run's progress *before*
                // the mode gate, so a run that was switched into Classic mid-session still starts its
                // next run from scratch rather than from a stale high-water mark.
                _highestPaidStreak = 0;
                return;
            }

            if (_gameModeModel != null && !_gameModeModel.ExtrasEnabled)
            {
                return;
            }

            int firstUnpaid = Math.Max(FIRST_PAYING_STREAK, _highestPaidStreak + 1);
            int lastToPay = Math.Min(streak, LAST_PAYING_STREAK);

            for (int level = firstUnpaid; level <= lastToPay; level++)
            {
                DropCoin(CoinValueForStreak(level));
            }

            if (lastToPay >= firstUnpaid)
            {
                _highestPaidStreak = lastToPay;
            }
        }

        private void DropCoin(int coinValue)
        {
            GridPosition? spawn = CoinSpawnSelector.SelectSpawnPosition(_boardModel.Board, _random);
            if (spawn == null)
            {
                // No block on the board to convert — a perfect clear that also advanced the streak.
                // Silently skipped, which is an ordinary outcome and not an error state; the level still
                // counts as paid, because it was reached and the reward simply had nowhere to land.
                return;
            }

            // Converted in place: the block keeps its colour and its occupancy, and the icon is what
            // marks it as special. Nothing is occupied here, so a placement that emptied the board
            // cannot have that achievement quietly taken back by its own reward.
            _boardModel.SetCoinCell(spawn.Value, coinValue);

            if (_specialCellSpawnedPublisher != null)
            {
                _specialCellSpawnedPublisher.Publish(
                    new SpecialCellSpawnedMessage(SpecialCellKind.Coin, spawn.Value));
            }
        }
    }
}
