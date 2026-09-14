using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="PowerUpModel"/>: earning, spending and persisting the power-up inventory, plus
    /// applying a spent power-up to <see cref="BoardModel"/>.
    /// <para>
    /// It deliberately does not score. Applying publishes <see cref="PowerUpAppliedMessage"/> and
    /// <see cref="PowerUpScoreSystem"/> turns that into points, mirroring how placements reach
    /// <see cref="ScoreSystem"/> — which is also what keeps power-up scoring out of the streak logic.
    /// </para>
    /// <para>
    /// Spending is charged for a valid target even when the target turns out to be empty: the player
    /// made a deliberate, legal application. Only holding none of the power-up is a true no-op —
    /// with one exception, <see cref="PowerUpKind.Joker"/>, which is the only kind that can be aimed
    /// at an illegal target at all (an already-occupied cell) and charges nothing for one.
    /// </para>
    /// <para>
    /// It also owns the armed selection: selecting a power-up arms it immediately (there is no queue),
    /// and the run's clock is held for as long as it stays armed. Arming, cancelling and applying all
    /// leave through <see cref="Disarm"/>, so "armed" and "the clock is held for it" can never drift
    /// apart.
    /// </para>
    /// </summary>
    public sealed class PowerUpSystem : IDisposable
    {
        /// <summary>
        /// Colour a joker's fill takes. Fixed rather than drawn: colour is cosmetic and never affects
        /// placement or clearing, and a constant makes a joker cell recognisably its own thing.
        /// </summary>
        private const int JOKER_FILL_COLOUR_ID = 1;

        private readonly PowerUpModel _powerUpModel;
        private readonly BoardModel _boardModel;
        private readonly BoardSystem _boardSystem;
        private readonly TimerRunSystem _timerRunSystem;
        private readonly IRewardSource _rewardSource;
        private readonly IPublisher<PowerUpAppliedMessage> _appliedPublisher;
        private readonly IPublisher<PowerUpGrantedMessage> _grantedPublisher;
        private readonly IDisposable _runStartedSubscription;
        private readonly IDisposable _gameOverSubscription;

        public PowerUpSystem(
            PowerUpModel powerUpModel,
            BoardModel boardModel,
            BoardSystem boardSystem,
            TimerRunSystem timerRunSystem,
            IRewardSource rewardSource,
            IPublisher<PowerUpAppliedMessage> appliedPublisher,
            IPublisher<PowerUpGrantedMessage> grantedPublisher,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber)
        {
            _powerUpModel = powerUpModel;
            _boardModel = boardModel;
            _boardSystem = boardSystem;
            _timerRunSystem = timerRunSystem;
            _rewardSource = rewardSource;
            _appliedPublisher = appliedPublisher;
            _grantedPublisher = grantedPublisher;

            LoadPersistedCount(PowerUpKind.Bomb);
            LoadPersistedCount(PowerUpKind.RowClear);
            LoadPersistedCount(PowerUpKind.ColumnClear);
            LoadPersistedCount(PowerUpKind.Joker);
            LoadPersistedCount(PowerUpKind.ColorCleanser);

            // An armed selection belongs to the run it was made in: it must not survive either end of
            // a run boundary, or the next run would open with a power-up already aimed and its clock
            // held.
            _runStartedSubscription = runStartedSubscriber.Subscribe(OnRunStarted);
            _gameOverSubscription = gameOverSubscriber.Subscribe(OnGameOver);
        }

        /// <summary>
        /// Selects <paramref name="kind"/> and aims it at the board immediately, holding the run's
        /// clock until it is applied or cancelled. Holding none of that kind, or a run that is already
        /// over, is a no-op: neither arms, so neither can be spent by a follow-up tap.
        /// </summary>
        public void Arm(PowerUpKind kind)
        {
            if (_boardSystem.IsGameOver || CountOf(kind).Value <= 0)
            {
                return;
            }

            _powerUpModel.Armed.Value = kind;
            _timerRunSystem.SetPowerUpArmedPaused(true);
        }

        /// <summary>Drops the armed selection without spending anything. A no-op when nothing is armed.</summary>
        public void CancelArm()
        {
            if (_powerUpModel.Armed.Value == null)
            {
                return;
            }

            Disarm();
        }

        /// <summary>Spends one bomb on the 3x3 area around <paramref name="center"/>. False when the
        /// player holds none or the target is off the board — nothing is changed in either case.</summary>
        public bool TryApplyBomb(GridPosition center)
        {
            if (!Board.IsInside(center) || !TrySpend(PowerUpKind.Bomb))
            {
                return false;
            }

            Apply(PowerUpKind.Bomb, PowerUpClearResolver.ResolveBombClear(_boardModel.Board, center));
            Disarm();
            return true;
        }

        /// <summary>Spends one row clear on <paramref name="row"/>, full or not.</summary>
        public bool TryApplyRowClear(int row)
        {
            if (!IsValidLineIndex(row) || !TrySpend(PowerUpKind.RowClear))
            {
                return false;
            }

            Apply(PowerUpKind.RowClear, PowerUpClearResolver.ResolveRowClear(_boardModel.Board, row));
            Disarm();
            return true;
        }

        /// <summary>Spends one column clear on <paramref name="column"/>, full or not.</summary>
        public bool TryApplyColumnClear(int column)
        {
            if (!IsValidLineIndex(column) || !TrySpend(PowerUpKind.ColumnClear))
            {
                return false;
            }

            Apply(PowerUpKind.ColumnClear, PowerUpClearResolver.ResolveColumnClear(_boardModel.Board, column));
            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one joker on <paramref name="target"/>: fills that cell, then clears its row and/or
        /// column if the fill completed them.
        /// <para>
        /// Unlike the other three kinds a joker has illegal targets — an off-board cell or an already
        /// occupied one. Those are refused outright: nothing is spent, nothing is disarmed and nothing
        /// is published, so the player simply aims again rather than losing the power-up to a misplaced
        /// tap. Only a real fill is charged for, whether or not it went on to clear anything.
        /// </para>
        /// </summary>
        public bool TryApplyJoker(GridPosition target)
        {
            // Peeked rather than spent: the fill below decides whether this tap is legal at all, and
            // an illegal one must leave the inventory exactly as it found it.
            if (CountOf(PowerUpKind.Joker).Value <= 0)
            {
                return false;
            }

            // Legality lives in the resolver, not here: a rejected result is the single, authoritative
            // statement that the board was not touched.
            JokerFillResult result = JokerFillResolver.ResolveFill(
                _boardModel.Board, target, JOKER_FILL_COLOUR_ID);
            if (!result.Filled)
            {
                return false;
            }

            TrySpend(PowerUpKind.Joker);

            _boardModel.NotifyFilled(result.Position, JOKER_FILL_COLOUR_ID);
            if (result.AnyCleared)
            {
                _boardModel.NotifyPowerUpCleared(result.ClearedCells);
            }

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                PowerUpKind.Joker, result.ClearedCellCount, result.LineCount));

            Disarm();
            return true;
        }

        /// <summary>
        /// Spends one colour cleanser on <paramref name="target"/>: clears every cell on the board
        /// sharing that cell's colour. An empty target has no colour to extract and is refused outright
        /// — the same "peek before spending" contract as <see cref="TryApplyJoker"/>: nothing is spent,
        /// nothing is disarmed, the player simply aims again.
        /// </summary>
        public bool TryApplyColorCleanser(GridPosition target)
        {
            // Peeked rather than spent, mirroring TryApplyJoker: legality here is "does the resolver
            // find a colour to clear", and that must be checked before a single count is touched.
            if (!Board.IsInside(target) || CountOf(PowerUpKind.ColorCleanser).Value <= 0)
            {
                return false;
            }

            PowerUpClearResult result = PowerUpClearResolver.ResolveColorCleanser(_boardModel.Board, target);
            if (!result.AnyCleared)
            {
                return false;
            }

            TrySpend(PowerUpKind.ColorCleanser);
            Apply(PowerUpKind.ColorCleanser, result);
            Disarm();
            return true;
        }

        /// <summary>Asks <see cref="IRewardSource"/> for one <paramref name="kind"/> and banks it if
        /// granted. Returns whether it was granted; a refusal leaves the inventory untouched.</summary>
        public async UniTask<bool> GrantRewardAsync(PowerUpKind kind, CancellationToken cancellationToken)
        {
            RewardResult result = await _rewardSource.RequestRewardAsync(kind, cancellationToken);
            if (!result.Granted)
            {
                return false;
            }

            Grant(result.Kind);
            return true;
        }

        /// <summary>
        /// Banks one <paramref name="kind"/> unconditionally and immediately, without asking
        /// <see cref="IRewardSource"/>. For rewards the player has already earned by playing — a badge
        /// unlock, say — where there is nothing left to gate on: the achievement *is* the grant, so
        /// routing it through the ad-watch seam would let a declined ad swallow a reward the player
        /// already won.
        /// <para>
        /// Shares <see cref="Grant"/> with <see cref="GrantRewardAsync"/>, so both paths mutate,
        /// persist and publish identically — there is exactly one place a grant happens.
        /// </para>
        /// </summary>
        public void GrantDirect(PowerUpKind kind) => Grant(kind);

        public void Dispose()
        {
            _runStartedSubscription.Dispose();
            _gameOverSubscription.Dispose();
        }

        /// <summary>Shared exit from armed mode: a successful application, an explicit cancel and both
        /// run boundaries all land here, so the clock is never left held by a selection that is gone.</summary>
        private void Disarm()
        {
            _powerUpModel.Armed.Value = null;
            _timerRunSystem.SetPowerUpArmedPaused(false);
        }

        private void OnRunStarted(RunStartedMessage message) => Disarm();

        private void OnGameOver(GameOverMessage message) => Disarm();

        /// <summary>Delegates to Core so the index a power-up will accept and the geometry the preview
        /// draws for it can never disagree about which rows/columns exist.</summary>
        private static bool IsValidLineIndex(int index) => PowerUpTargetCells.IsValidLineIndex(index);

        /// <summary>
        /// The one and only way a power-up enters the inventory: increment, persist, announce. Every
        /// earning path funnels through here so the three steps can never drift out of step with each
        /// other, whatever gate (or lack of one) got the player this far.
        /// </summary>
        private void Grant(PowerUpKind kind)
        {
            ReactiveProperty<int> count = CountOf(kind);
            count.Value += 1;
            Persist(kind, count.Value);
            _grantedPublisher.Publish(new PowerUpGrantedMessage(kind, count.Value));
        }

        /// <summary>Decrements and persists the inventory, or reports that there was none to spend.</summary>
        private bool TrySpend(PowerUpKind kind)
        {
            ReactiveProperty<int> count = CountOf(kind);
            if (count.Value <= 0)
            {
                return false;
            }

            count.Value -= 1;
            Persist(kind, count.Value);
            return true;
        }

        private void Apply(PowerUpKind kind, PowerUpClearResult result)
        {
            if (result.AnyCleared)
            {
                _boardModel.NotifyPowerUpCleared(result.ClearedCells);
            }

            _appliedPublisher.Publish(new PowerUpAppliedMessage(
                kind, result.ClearedCellCount, clearedLineCount: 0, emptiedLineCount: result.EmptiedLineCount));
        }

        private ReactiveProperty<int> CountOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return _powerUpModel.RowClearCount;
                case PowerUpKind.ColumnClear:
                    return _powerUpModel.ColumnClearCount;
                case PowerUpKind.Joker:
                    return _powerUpModel.JokerCount;
                case PowerUpKind.ColorCleanser:
                    return _powerUpModel.ColorCleanserCount;
                default:
                    return _powerUpModel.BombCount;
            }
        }

        private void LoadPersistedCount(PowerUpKind kind)
        {
            CountOf(kind).Value = PlayerPrefs.GetInt(PowerUpInventoryKey.For(kind), 0);
        }

        private static void Persist(PowerUpKind kind, int count)
        {
            PlayerPrefs.SetInt(PowerUpInventoryKey.For(kind), count);
        }
    }
}
