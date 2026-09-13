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
    /// made a deliberate, legal application. Only holding none of the power-up is a true no-op.
    /// </para>
    /// </summary>
    public sealed class PowerUpSystem
    {
        private readonly PowerUpModel _powerUpModel;
        private readonly BoardModel _boardModel;
        private readonly IRewardSource _rewardSource;
        private readonly IPublisher<PowerUpAppliedMessage> _appliedPublisher;
        private readonly IPublisher<PowerUpGrantedMessage> _grantedPublisher;

        public PowerUpSystem(
            PowerUpModel powerUpModel,
            BoardModel boardModel,
            IRewardSource rewardSource,
            IPublisher<PowerUpAppliedMessage> appliedPublisher,
            IPublisher<PowerUpGrantedMessage> grantedPublisher)
        {
            _powerUpModel = powerUpModel;
            _boardModel = boardModel;
            _rewardSource = rewardSource;
            _appliedPublisher = appliedPublisher;
            _grantedPublisher = grantedPublisher;

            LoadPersistedCount(PowerUpKind.Bomb);
            LoadPersistedCount(PowerUpKind.RowClear);
            LoadPersistedCount(PowerUpKind.ColumnClear);
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

            ReactiveProperty<int> count = CountOf(result.Kind);
            count.Value += 1;
            Persist(result.Kind, count.Value);
            _grantedPublisher.Publish(new PowerUpGrantedMessage(result.Kind, count.Value));
            return true;
        }

        private static bool IsValidLineIndex(int index) => index >= 0 && index < Board.SIZE;

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

            _appliedPublisher.Publish(new PowerUpAppliedMessage(kind, result.ClearedCellCount));
        }

        private ReactiveProperty<int> CountOf(PowerUpKind kind)
        {
            switch (kind)
            {
                case PowerUpKind.RowClear:
                    return _powerUpModel.RowClearCount;
                case PowerUpKind.ColumnClear:
                    return _powerUpModel.ColumnClearCount;
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
