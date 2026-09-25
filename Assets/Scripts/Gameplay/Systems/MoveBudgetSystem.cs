using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="MoveBudgetModel"/>: the move budget of a Path level whose objective is "reach S
    /// points within N moves" (issue #465, <see cref="ObjectiveType.ScoreInMoves"/>).
    /// <para>
    /// A move is one board placement — one <see cref="PiecePlacedMessage"/>, which only a placement that
    /// actually landed publishes, so a refused or illegal drop never costs one and no placement counts
    /// twice. Power-ups, Rotate, Reroll and Hold publish no placement, so they never spend a move, and
    /// the no-moves rescue (#461) re-deals the dock without touching the count.
    /// </para>
    /// <para>
    /// When the last move is spent short of the target the run is <em>held</em>, not ended: the one
    /// rewarded "+moves" offer of this attempt opens (<see cref="MovesRanOutMessage"/>) and
    /// <see cref="BoardSystem"/> refuses placements and skips its dead-dock check until the player
    /// answers. A granted ad adds <see cref="MoveBudgetConfig.ExtraMovesFromAd"/> moves; a decline or an
    /// unpaid ad fails the run as <see cref="GameOverReason.ObjectiveMissed"/>, the timer cell's ending.
    /// The second time the budget runs out there is no offer and the run fails at once.
    /// </para>
    /// <para>
    /// <b>Subscription order is load-bearing.</b> This System must be resolved after
    /// <see cref="ObjectiveSystem"/> and <see cref="LevelProgressionSystem"/>: MessagePipe calls
    /// <see cref="PiecePlacedMessage"/> handlers in subscription order, and it is ObjectiveSystem's
    /// handler that completes the objective — raising <see cref="PathRunModel.IsLevelCompleting"/>
    /// synchronously — which is how the last move that reaches the target is told apart from a last move
    /// that falls short.
    /// </para>
    /// </summary>
    public sealed class MoveBudgetSystem : IDisposable
    {
        private readonly MoveBudgetModel _model;
        private readonly MoveBudgetConfig _config;
        private readonly ObjectiveModel _objectiveModel;
        private readonly GameModeModel _gameModeModel;
        private readonly PathRunModel _pathRunModel;
        private readonly BoardSystem _boardSystem;
        private readonly IExtraMovesRewardSource _rewardSource;
        private readonly IPublisher<MovesRanOutMessage> _movesRanOutPublisher;
        private readonly IDisposable _subscriptions;

        /// <summary>True from an ad request until its answer is acted on, so a second tap cannot queue a
        /// second ad.</summary>
        private bool _isRequestPending;

        public MoveBudgetSystem(
            MoveBudgetModel model,
            MoveBudgetConfig config,
            ObjectiveModel objectiveModel,
            GameModeModel gameModeModel,
            PathRunModel pathRunModel,
            BoardSystem boardSystem,
            IExtraMovesRewardSource rewardSource,
            ISubscriber<RunStartedMessage> runStartedSubscriber,
            ISubscriber<PiecePlacedMessage> piecePlacedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            IPublisher<MovesRanOutMessage> movesRanOutPublisher)
        {
            _model = model;
            _config = config;
            _objectiveModel = objectiveModel;
            _gameModeModel = gameModeModel;
            _pathRunModel = pathRunModel;
            _boardSystem = boardSystem;
            _rewardSource = rewardSource;
            _movesRanOutPublisher = movesRanOutPublisher;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            piecePlacedSubscriber.Subscribe(OnPiecePlaced).AddTo(bag);
            gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>Whether a "+moves" ad can be asked for right now.</summary>
        public bool CanRequestExtraMoves => _model.IsOfferOpen.Value && !_isRequestPending;

        /// <summary>
        /// Asks the reward source for the offer's extra moves. Granted: the moves are added, the offer
        /// closes and the dock is re-checked (the budget was the only thing holding the run). Refused or
        /// failed: the run fails as <see cref="GameOverReason.ObjectiveMissed"/>. Cancelled (the caller went
        /// away): nothing changes and the offer stays up. Returns whether the moves were added.
        /// </summary>
        public async UniTask<bool> TryRequestExtraMovesAsync(CancellationToken cancellationToken)
        {
            if (!CanRequestExtraMoves || _rewardSource == null)
            {
                return false;
            }

            _isRequestPending = true;
            bool granted;
            try
            {
                ExtraMovesRewardResult result = await _rewardSource.RequestExtraMovesRewardAsync(cancellationToken);
                granted = result.Granted;
            }
            catch (OperationCanceledException)
            {
                _isRequestPending = false;
                return false;
            }
            catch (Exception)
            {
                // An ad SDK that errors is read as an unpaid ad for the run's sake, and the error goes on to
                // the caller to log, as BoardSystem's rescue lets its source's errors through.
                _isRequestPending = false;
                DeclineExtraMoves();
                throw;
            }

            _isRequestPending = false;

            // Re-read after the await: the run may have ended or restarted while the ad was up.
            if (!_model.IsOfferOpen.Value)
            {
                return false;
            }

            if (!granted)
            {
                DeclineExtraMoves();
                return false;
            }

            _model.ExtraMoves += _config.ExtraMovesFromAd;
            _model.IsOfferSpent = true;
            SyncMovesLeft();
            _model.IsOfferOpen.Value = false;

            // The run was held, not checked, while the offer was up — a dock nothing fits in is only
            // noticed now, and ends the run the usual rescuable way.
            _boardSystem.RecheckGameOver();
            return true;
        }

        /// <summary>The player turned the offer down: the run fails as ObjectiveMissed. A no-op when no
        /// offer is up.</summary>
        public void DeclineExtraMoves()
        {
            if (!_model.IsOfferOpen.Value)
            {
                return;
            }

            _model.IsOfferSpent = true;
            _model.IsOfferOpen.Value = false;
            _boardSystem.ForceGameOver(GameOverReason.ObjectiveMissed);
        }

        /// <summary>
        /// Records and returns the leftover-move bonus of a cleared level: every move still unspent pays
        /// <see cref="MoveBudgetConfig.BonusPerLeftoverMove"/>. Called once by
        /// <see cref="LevelProgressionSystem"/> as the level completes, before the run's final score is
        /// read. 0 for a run without a budget.
        /// </summary>
        internal int ClaimLeftoverBonus()
        {
            if (!_model.IsActive.Value)
            {
                return 0;
            }

            _model.LeftoverMoves = _model.MovesLeft.Value;
            _model.LeftoverBonus = _model.LeftoverMoves * _config.BonusPerLeftoverMove;
            return _model.LeftoverBonus;
        }

        /// <summary>
        /// Arms the budget for a Path run on a level with a ScoreInMoves objective, and disarms it for
        /// every other run. The objectives are applied before the run starts
        /// (<see cref="LevelProgressionSystem.TryStartPathLevel"/>), so the tracked set is this level's.
        /// </summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            int moveLimit = _gameModeModel.CurrentMode.Value == GameMode.Path ? FindMoveLimit() : 0;

            _model.MoveLimit = moveLimit;
            _model.MovesUsed = 0;
            _model.ExtraMoves = 0;
            _model.IsOfferSpent = false;
            _model.LeftoverMoves = 0;
            _model.LeftoverBonus = 0;
            _model.IsOfferOpen.Value = false;
            SyncMovesLeft();
            _model.IsActive.Value = moveLimit > 0;
        }

        private int FindMoveLimit()
        {
            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                ObjectiveDefinition definition = objectives[objectiveIndex].Definition;
                if (definition.Type == ObjectiveType.ScoreInMoves)
                {
                    return definition.MoveLimit;
                }
            }

            return 0;
        }

        private void OnPiecePlaced(PiecePlacedMessage message)
        {
            if (!_model.IsActive.Value)
            {
                return;
            }

            _model.MovesUsed++;
            SyncMovesLeft();

            if (_model.MovesLeft.Value > 0)
            {
                return;
            }

            // The move that reached the target has already started the level's completion (see the
            // class summary on order); a run a timer cell or anything else already ended is no business
            // of the budget's.
            if (_boardSystem.IsGameOver || _pathRunModel.IsLevelCompleting)
            {
                return;
            }

            if (!_model.IsOfferSpent && _rewardSource != null)
            {
                _model.IsOfferOpen.Value = true;
                _movesRanOutPublisher.Publish(new MovesRanOutMessage());
                return;
            }

            _boardSystem.ForceGameOver(GameOverReason.ObjectiveMissed);
        }

        /// <summary>
        /// Takes the offer down if the run ended under it some other way — a timer cell expiring on the
        /// very placement that spent the budget, say. The offer is then spent: the ending stands.
        /// </summary>
        private void OnGameOver(GameOverMessage message)
        {
            if (!_model.IsOfferOpen.Value)
            {
                return;
            }

            _model.IsOfferSpent = true;
            _model.IsOfferOpen.Value = false;
        }

        private void SyncMovesLeft()
        {
            int movesLeft = _model.MoveLimit + _model.ExtraMoves - _model.MovesUsed;
            _model.MovesLeft.Value = movesLeft > 0 ? movesLeft : 0;
        }
    }
}
