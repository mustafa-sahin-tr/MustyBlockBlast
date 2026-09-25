using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="LevelProgressionModel"/>: which level the player is on, the objective that
    /// level asks for, and the persistence of both.
    /// <para>
    /// Progression is linear and gated by construction rather than by a check: the model holds a
    /// single current level and the only mutation is "+1, on completion of the current level's
    /// objective", so there is no code path that could skip a level — reaching N+2 requires clearing
    /// N+1, which requires having been on it.
    /// </para>
    /// <para>
    /// It never touches placements. <see cref="ObjectiveSystem"/> remains the only writer of objective
    /// progress; this system only decides which objective is the current one and rehydrates its saved
    /// value, so the rules engine and the content layer stay separable.
    /// </para>
    /// <para>
    /// It also pays out the level-up power-up reward, because advancing is the event that earns it and
    /// this is the only place advancing happens. Every level rewards; which kinds is a rule in code —
    /// see <see cref="LevelCompletionRewards"/> (issue #462).
    /// </para>
    /// <para>
    /// <b>Path mode.</b> <see cref="GameMode.Path"/> plays the same authored levels under a different
    /// contract: a run is bounded to exactly one level, and completing that level's objective ends the
    /// run instead of silently rolling onto the next objective. This system owns the second half of
    /// that too, because both halves are the same question — "which level is this, and what happens
    /// when it is cleared" — and splitting them would give that question two owners.
    /// </para>
    /// <para>
    /// The two notions of "which level" are kept strictly apart. <see cref="LevelProgressionModel"/>
    /// stays the persisted linear <em>frontier</em> and is still only ever moved by a first clear;
    /// <see cref="PathRunModel.ActiveLevelNumber"/> is the level the Path run in progress is playing,
    /// which may be any already-unlocked level the player tapped. Replaying a cleared level therefore
    /// cannot rewrite the frontier, re-pay its power-up, or push the player past content they have not
    /// reached — see <see cref="TryAdvanceFrontierAfterPathLevel"/>.
    /// </para>
    /// <para>
    /// Ending the run is a request to <see cref="BoardSystem.ForceGameOver"/> rather than a flag set
    /// here, for the same reason the timed clock's expiry is: the board system is the single owner of
    /// the "this run is over" invariant.
    /// </para>
    /// </summary>
    public sealed class LevelProgressionSystem : IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole JSON save blob — see <see cref="LevelProgressSaveData"/>.</summary>
        private const string SAVE_KEY = "Levels.Progress";

        private const int FIRST_LEVEL_NUMBER = 1;

        /// <summary>
        /// Upper bound on how long <see cref="CompletePathLevelAsync"/> waits for the board view to
        /// finish counting the empty cells (issue #424). The count-up's length scales with how many
        /// cells there are, so this is generous for a whole empty board; it exists only so a missing or
        /// misbehaving view can never leave a cleared level's run hanging without its result screen.
        /// </summary>
        private static readonly TimeSpan EmptyCellBonusCountingTimeout = TimeSpan.FromSeconds(6f);

        /// <summary>
        /// Upper bound on how long <see cref="CompletePathLevelAsync"/> waits for the special cells won on
        /// the final move to finish flying onto the board. Several flights of ~0.5 s each, generously; it
        /// exists only so a missing view can never hold a cleared level's result screen hostage.
        /// </summary>
        private static readonly TimeSpan PendingFlightsTimeout = TimeSpan.FromSeconds(5f);

        private readonly LevelProgressionModel _progressionModel;
        private readonly InfoPopupModel _infoPopupModel;
        private readonly PathRunModel _pathRunModel;
        private readonly ObjectiveModel _objectiveModel;
        private readonly BoardModel _boardModel;
        private readonly LevelCatalog _levelCatalog;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly GameModeSystem _gameModeSystem;
        private readonly BoardSystem _boardSystem;
        private readonly ScoreSystem _scoreSystem;
        private readonly LivesSystem _livesSystem;

        /// <summary>Pays a "score within N moves" level's leftover moves (issue #465); null in tests that
        /// have no budget.</summary>
        private readonly MoveBudgetSystem _moveBudgetSystem;
        private readonly IPublisher<LevelAdvancedMessage> _levelAdvancedPublisher;
        private readonly IPublisher<EmptyCellBonusCountingMessage> _emptyCellBonusCountingPublisher;
        private readonly ISubscriber<EmptyCellBonusCountingCompletedMessage> _emptyCellBonusCountingCompletedSubscriber;
        private readonly IPublisher<BonusScoredMessage> _bonusScoredPublisher;
        private readonly IPublisher<PendingFlightsDrainMessage> _pendingFlightsDrainPublisher;
        private readonly ISubscriber<PendingFlightsDrainedMessage> _pendingFlightsDrainedSubscriber;
        private readonly IDisposable _subscriptions;
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
        private readonly LevelProgressSaveData _saveData;

        /// <summary>
        /// Ids of the current level's objectives whose completion has already been announced. Cleared
        /// whenever the tracked objectives are replaced, so it only ever describes the level in play —
        /// see <see cref="AllTrackedObjectivesComplete"/> for why the announcement is remembered rather
        /// than re-read off the model every time.
        /// </summary>
        private readonly HashSet<string> _completedObjectiveIds = new HashSet<string>();

        /// <summary>Reused by <see cref="ApplyLevelObjective"/> so applying a level's objectives does
        /// not allocate a list per level change.</summary>
        private readonly List<ObjectiveProgress> _objectiveBuffer = new List<ObjectiveProgress>(2);

        /// <summary>
        /// Whether the mode the last <see cref="OnModeChanged"/> saw was <see cref="GameMode.Path"/>.
        /// Held so a switch between Endless and Timed — neither of which this system has any business
        /// reacting to — can be told apart from one that enters or leaves Path mode, and skipped.
        /// </summary>
        private bool _wasPathMode;

        public LevelProgressionSystem(
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            ObjectiveModel objectiveModel,
            BoardModel boardModel,
            LevelCatalog levelCatalog,
            PowerUpSystem powerUpSystem,
            GameModeSystem gameModeSystem,
            BoardSystem boardSystem,
            ScoreSystem scoreSystem,
            ISubscriber<ObjectiveCompletedMessage> objectiveCompletedSubscriber,
            ISubscriber<ObjectiveProgressChangedMessage> objectiveProgressChangedSubscriber,
            IPublisher<LevelAdvancedMessage> levelAdvancedPublisher,
            IPublisher<EmptyCellBonusCountingMessage> emptyCellBonusCountingPublisher,
            ISubscriber<EmptyCellBonusCountingCompletedMessage> emptyCellBonusCountingCompletedSubscriber,
            IPublisher<BonusScoredMessage> bonusScoredPublisher,
            IPublisher<PendingFlightsDrainMessage> pendingFlightsDrainPublisher,
            ISubscriber<PendingFlightsDrainedMessage> pendingFlightsDrainedSubscriber,
            InfoPopupModel infoPopupModel,
            LivesSystem livesSystem,
            MoveBudgetSystem moveBudgetSystem = null)
        {
            _moveBudgetSystem = moveBudgetSystem;
            _infoPopupModel = infoPopupModel;
            _livesSystem = livesSystem;
            _pendingFlightsDrainPublisher = pendingFlightsDrainPublisher;
            _pendingFlightsDrainedSubscriber = pendingFlightsDrainedSubscriber;
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
            _objectiveModel = objectiveModel;
            _boardModel = boardModel;
            _levelCatalog = levelCatalog;
            _powerUpSystem = powerUpSystem;
            _gameModeSystem = gameModeSystem;
            _boardSystem = boardSystem;
            _scoreSystem = scoreSystem;
            _levelAdvancedPublisher = levelAdvancedPublisher;
            _emptyCellBonusCountingPublisher = emptyCellBonusCountingPublisher;
            _emptyCellBonusCountingCompletedSubscriber = emptyCellBonusCountingCompletedSubscriber;
            _bonusScoredPublisher = bonusScoredPublisher;

            _saveData = Load();
            _progressionModel.CurrentLevelNumber.Value = ClampToCatalog(_saveData.currentLevelNumber);

            // Restores the saved value for a cumulative objective. Silent by design: nothing happened
            // this session, so no progress/completion message is published for the rehydration.
            ApplyLevelObjective(_progressionModel.CurrentLevelNumber.Value, restoreSavedProgress: true);

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            objectiveCompletedSubscriber.Subscribe(OnObjectiveCompleted).AddTo(bag);
            objectiveProgressChangedSubscriber.Subscribe(OnObjectiveProgressChanged).AddTo(bag);
            _gameModeSystem.CurrentMode.Subscribe(OnModeChanged).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose()
        {
            _subscriptions.Dispose();
            _disposeCts.Cancel();
            _disposeCts.Dispose();
        }

        /// <summary>
        /// Starts a Path-mode run at <paramref name="levelNumber"/>, as the level path overlay's node
        /// tap does. Returns false — changing nothing at all — when the request is not one this mode
        /// accepts: outside <see cref="GameMode.Path"/>, for a level past the unlocked frontier, or for
        /// a level the catalog does not author — and, since issue #478, at zero lives, when
        /// <see cref="LivesSystem.TryPassStartGate"/> refuses and opens the out-of-lives sheet instead.
        /// <para>
        /// Refusing outside Path mode rather than switching into it is what keeps a node read-only in
        /// Endless and Timed. Switching mode restarts the run, and a status light must not be able to
        /// throw away a run the player is in the middle of.
        /// </para>
        /// <para>
        /// The objective is applied before the run is started, so the reset
        /// <see cref="ObjectiveSystem"/> performs on <c>RunStartedMessage</c> lands on this level's
        /// objective rather than the previous one's.
        /// </para>
        /// </summary>
        public bool TryStartPathLevel(int levelNumber)
        {
            if (_gameModeSystem.CurrentMode.Value != GameMode.Path)
            {
                return false;
            }

            if (!IsUnlocked(levelNumber) || _levelCatalog.Find(levelNumber) == null)
            {
                return false;
            }

            // After the request is known to be a legal one — an out-of-lives sheet for a locked node would
            // be the wrong answer — and before anything below moves: a refusal must leave the walk, the
            // active level, the objective and the board exactly as they were (issue #478).
            if (!_livesSystem.TryPassStartGate())
            {
                return false;
            }

            // Going back to the top of the ladder is what "a fresh walk of the path" means, so the
            // running tally starts over with it. Every other entry point continues the walk in
            // progress, which is why this is the only place the total is cleared other than leaving
            // Path mode altogether.
            if (levelNumber == FIRST_LEVEL_NUMBER)
            {
                _pathRunModel.ResetWalk();
            }

            _pathRunModel.ActiveLevelNumber.Value = levelNumber;

            // Always a fresh objective, regardless of scope — a Path run is a bounded attempt at this
            // one level, not a continuation of whatever Endless/Timed session last touched it. Restoring
            // a Cumulative objective's saved value here would mean replaying an already-cleared level
            // hands back an objective that is already complete (RestoreProgress sets IsComplete from the
            // saved value), so the run could only ever end in NoMovesLeft, never a success.
            ApplyLevelObjective(levelNumber, restoreSavedProgress: false);
            _boardSystem.StartNewRun();
            return true;
        }

        /// <summary>
        /// Whether <paramref name="levelNumber"/> is one the player has reached. Progression is
        /// strictly linear, so this is a comparison against the frontier rather than a stored set —
        /// the same rule the level path overlay paints its nodes with.
        /// </summary>
        public bool IsUnlocked(int levelNumber)
            => levelNumber >= FIRST_LEVEL_NUMBER && levelNumber <= _progressionModel.CurrentLevelNumber.Value;

        /// <summary>
        /// Keeps the tracked objective and the path bookkeeping honest across a mode switch.
        /// <para>
        /// Switching between Endless and Timed is explicitly ignored: neither mode has ever cared which
        /// objective is tracked, and re-applying it for them would be a behaviour change they did not
        /// ask for. Only entering or leaving Path mode does anything here.
        /// </para>
        /// <para>
        /// <c>ReactiveProperty.Subscribe</c> fires immediately with the current value, which
        /// at construction is Endless — so the first call is one of the ignored ones and the explicit
        /// rehydration in the constructor stands.
        /// </para>
        /// </summary>
        private void OnModeChanged(GameMode mode)
        {
            bool isPathMode = mode == GameMode.Path;
            if (!isPathMode && !_wasPathMode)
            {
                return;
            }

            _wasPathMode = isPathMode;

            // A walk of the path is the span between entering and leaving Path mode, so its tally
            // starts (and is discarded) at those two edges.
            _pathRunModel.ResetWalk();

            if (!isPathMode)
            {
                _pathRunModel.ActiveLevelNumber.Value = PathRunModel.NO_ACTIVE_LEVEL;
                ApplyLevelObjective(_progressionModel.CurrentLevelNumber.Value, restoreSavedProgress: true);
                return;
            }

            // Entering Path mode drops the player on the level they have reached; the overlay's nodes
            // are how they go anywhere else. GameModeSystem restarts the run immediately after this
            // returns, so the objective set here is the one that run is played against. Fresh, not
            // restored — same reasoning as TryStartPathLevel: this is a bounded Path attempt, not a
            // continuation of Endless/Timed's cumulative tally for this level.
            _pathRunModel.ActiveLevelNumber.Value = _progressionModel.CurrentLevelNumber.Value;
            ApplyLevelObjective(_pathRunModel.ActiveLevelNumber.Value, restoreSavedProgress: false);
        }

        /// <summary>
        /// Advancing is driven by the completion message rather than polled, so the level moves on in
        /// the same frame the qualifying placement resolved. Ignores completions belonging to anything
        /// other than the current level's objective, which is what makes a stale message from a
        /// previous level harmless.
        /// <para>
        /// In Path mode the completion is the end of the run rather than a step within it, so the two
        /// outcomes fork here and nowhere else — everything below this method is the Endless/Timed
        /// behaviour, unchanged.
        /// </para>
        /// </summary>
        private void OnObjectiveCompleted(ObjectiveCompletedMessage message)
        {
            if (!IsTrackedObjective(message.ObjectiveId))
            {
                return;
            }

            _completedObjectiveIds.Add(message.ObjectiveId);

            // AND, not OR: a level asking for several objectives is cleared by the last of them, not by
            // the first. Sited here rather than in ObjectiveSystem because "what counts as clearing a
            // level" is progression's question — the rules engine only ever reports one objective at a
            // time and has no idea they were authored together.
            if (!AllTrackedObjectivesComplete())
            {
                return;
            }

            if (_gameModeSystem.CurrentMode.Value == GameMode.Path)
            {
                CompletePathLevelAsync().Forget();
                return;
            }

            AdvanceToNextLevel();
        }

        /// <summary>
        /// Ends a Path-mode run as a success: pays the level's score bonus plus one point per empty
        /// cell left on the board (issue #424) into the run that earned them, folds that run's finished
        /// score into the walk's running total, banks a first clear, and only then declares the run over.
        /// <para>
        /// The empty-cell bonus rewards finishing the objective with room to spare, so the board is
        /// asked to count those cells out loud first — see <see cref="EmptyCellBonusCountingMessage"/> —
        /// and this waits (bounded by <see cref="EmptyCellBonusCountingTimeout"/>) for that count-up to
        /// finish before anything below runs. The result screen is what
        /// <see cref="BoardSystem.ForceGameOver"/> brings up, so it cannot appear until the count has
        /// been shown.
        /// </para>
        /// <para>
        /// The order matters. Both bonuses have to be inside <c>ScoreModel.Score</c> before
        /// <see cref="BoardSystem.ForceGameOver"/> publishes, because that score is what the end-of-run
        /// card reads and what the path tally sums — paying them afterwards would show the player a
        /// total that is short by exactly the reward they just earned.
        /// </para>
        /// </summary>
        private async UniTaskVoid CompletePathLevelAsync()
        {
            // Raised for the whole sequence, cleared however it ends (screen shown or scope torn down) —
            // see PathRunModel.IsLevelCompleting.
            _pathRunModel.IsLevelCompleting = true;
            try
            {
                await CompletePathLevelSequenceAsync();
            }
            finally
            {
                _pathRunModel.IsLevelCompleting = false;
            }
        }

        private async UniTask CompletePathLevelSequenceAsync()
        {
            int completedLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            LevelObjectiveConfig completedLevel = _levelCatalog.Find(completedLevelNumber);
            int configBonus = completedLevel != null ? completedLevel.CompletionScoreBonus : 0;

            // Every move left on a "score within N moves" level pays its bonus (issue #465), into the same
            // score and the same "+N" flight as the empty cells. Counted now, as the target is reached,
            // before the waits below — a piece dropped while the flights land must not eat into it.
            int leftoverMovesBonus = _moveBudgetSystem != null ? _moveBudgetSystem.ClaimLeftoverBonus() : 0;

            // Special cells won on the final move fly onto the board first — and a first-time one's
            // explainer, which opens as it lands, is closed — before the empty cells are counted; the
            // result screen comes after that. Nothing ever plays over anything else.
            try
            {
                await AwaitRequestAsync(
                    () => _pendingFlightsDrainPublisher.Publish(new PendingFlightsDrainMessage()),
                    _pendingFlightsDrainedSubscriber, PendingFlightsTimeout, _disposeCts.Token);
                await WaitForInfoPopupClosedAsync(_disposeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Disposed (scene/scope tearing down) mid-wait — there is no run left to end.
                return;
            }

            Board board = _boardModel.Board;
            int emptyCellBonus = Math.Max(0, board.PlayableCellCount - board.OccupiedCellCount());

            if (emptyCellBonus > 0)
            {
                try
                {
                    await RequestEmptyCellBonusCountingAsync(emptyCellBonus, _disposeCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Disposed (scene/scope tearing down) mid-count — there is no run left to end.
                    return;
                }
            }

            int finalScore = _scoreSystem.AddLevelCompletionBonus(configBonus + emptyCellBonus + leftoverMovesBonus);
            _pathRunModel.RecordLevelCompletion(completedLevelNumber, finalScore);

            // Reuses the same "+N flies to the score counter" feedback every other bonus already gets
            // (BonusFeedbackView/BonusSfxView, issue #329) — the on-board count-up already told the
            // player how many empty cells they earned; this is what tells them those points actually
            // landed in the score they see at the top of the screen (issue #424). The flat, unannounced
            // per-level CompletionScoreBonus is deliberately left out of this popup: it predates this
            // feedback and changing its presentation is not this issue's concern.
            int announcedBonus = emptyCellBonus + leftoverMovesBonus;
            if (announcedBonus > 0)
            {
                _bonusScoredPublisher.Publish(new BonusScoredMessage(announcedBonus));
            }

            TryAdvanceFrontierAfterPathLevel(completedLevelNumber);

            // The rewards just paid fly in ("YOU WON!") and a first-time power-up opens its explainer
            // when it lands; the result card waits for both, so the player reads what they won before
            // the level-complete screen comes up rather than on top of it.
            try
            {
                await AwaitRequestAsync(
                    () => _pendingFlightsDrainPublisher.Publish(new PendingFlightsDrainMessage()),
                    _pendingFlightsDrainedSubscriber, PendingFlightsTimeout, _disposeCts.Token);
                await WaitForInfoPopupClosedAsync(_disposeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Disposed (scene/scope tearing down) mid-wait — there is no run left to end.
                return;
            }

            _boardSystem.ForceGameOver(GameOverReason.LevelCompleted);
        }

        /// <summary>
        /// Completes once no info popup is open — at once if none is. No timeout: the popup is the
        /// player's to dismiss, and the level-complete card simply follows it.
        /// </summary>
        private async UniTask WaitForInfoPopupClosedAsync(CancellationToken cancellationToken)
        {
            if (_infoPopupModel.OpenContent.Value == null)
            {
                return;
            }

            UniTaskCompletionSource<bool> closed = new UniTaskCompletionSource<bool>();
            IDisposable subscription = _infoPopupModel.OpenContent.Subscribe(content =>
            {
                if (content == null)
                {
                    closed.TrySetResult(true);
                }
            });

            try
            {
                await closed.Task.AttachExternalCancellation(cancellationToken);
            }
            finally
            {
                subscription.Dispose();
            }
        }

        /// <summary>
        /// Publishes <see cref="EmptyCellBonusCountingMessage"/> and awaits the next
        /// <see cref="EmptyCellBonusCountingCompletedMessage"/>, or
        /// <see cref="EmptyCellBonusCountingTimeout"/>, whichever comes first. The completion is
        /// subscribed to <em>before</em> the request goes out, so a view that answers synchronously
        /// (nothing to show) is not missed. Modelled on <c>InfoPopupSystem</c>'s grant-animation wait.
        /// </summary>
        private UniTask RequestEmptyCellBonusCountingAsync(int emptyCellCount, CancellationToken cancellationToken)
            => AwaitRequestAsync(
                () => _emptyCellBonusCountingPublisher.Publish(new EmptyCellBonusCountingMessage(emptyCellCount)),
                _emptyCellBonusCountingCompletedSubscriber, EmptyCellBonusCountingTimeout, cancellationToken);

        /// <summary>
        /// Publishes a request (via <paramref name="publishRequest"/>) and awaits the next
        /// <typeparamref name="TReply"/>, or <paramref name="timeout"/>, whichever comes first. The reply
        /// is subscribed to <em>before</em> the request goes out, so a view that answers synchronously
        /// is not missed — and then this returns in the same frame.
        /// </summary>
        private static async UniTask AwaitRequestAsync<TReply>(
            Action publishRequest, ISubscriber<TReply> replySubscriber, TimeSpan timeout, CancellationToken cancellationToken)
        {
            UniTaskCompletionSource<bool> completionSource = new UniTaskCompletionSource<bool>();
            IDisposable subscription = replySubscriber.Subscribe(_ => completionSource.TrySetResult(true));

            try
            {
                publishRequest();

                if (completionSource.Task.Status == UniTaskStatus.Succeeded)
                {
                    return;
                }

                await UniTask.WhenAny(
                    completionSource.Task.AttachExternalCancellation(cancellationToken),
                    UniTask.Delay(timeout, cancellationToken: cancellationToken));
            }
            finally
            {
                subscription.Dispose();
            }
        }

        /// <summary>
        /// Banks a Path-mode clear against the linear frontier, but only when the level cleared <em>is
        /// </em> the frontier — i.e. this was a first clear rather than a replay.
        /// <para>
        /// That guard is the whole point of keeping the two level numbers apart. Without it, replaying
        /// level 3 while standing at level 9 would drag the frontier back to 4 (losing six levels of
        /// progress) and re-pay level 3's power-up every time, turning an already-cleared level into a
        /// reward farm.
        /// </para>
        /// <para>
        /// Deliberately does <em>not</em> re-apply the objective, unlike <see cref="AdvanceToNextLevel"/>:
        /// the run is about to end on the level it was played for, and the end-of-run card still
        /// describes that level. The next Path run sets its own objective when a node starts it.
        /// </para>
        /// </summary>
        private void TryAdvanceFrontierAfterPathLevel(int completedLevelNumber)
        {
            if (completedLevelNumber != _progressionModel.CurrentLevelNumber.Value)
            {
                return;
            }

            int nextLevelNumber = completedLevelNumber + 1;
            if (_levelCatalog.Find(nextLevelNumber) == null)
            {
                // Out of content: the frontier stays on the last level, exactly as it does in the
                // Endless/Timed path. The run still ends as a success and still pays its bonus — the
                // player cleared the level either way.
                return;
            }

            _progressionModel.CurrentLevelNumber.Value = nextLevelNumber;
            _saveData.currentLevelNumber = nextLevelNumber;
            Save();

            GrantLevelUpReward(completedLevelNumber);

            _levelAdvancedPublisher.Publish(new LevelAdvancedMessage(nextLevelNumber));
        }

        /// <summary>
        /// The Endless/Timed behaviour: clearing the current level's objective rolls the tracked
        /// objective onto the next level without ending the run.
        /// </summary>
        private void AdvanceToNextLevel()
        {
            int completedLevelNumber = _progressionModel.CurrentLevelNumber.Value;
            int nextLevelNumber = completedLevelNumber + 1;
            if (_levelCatalog.Find(nextLevelNumber) == null)
            {
                // Out of content: the player stays on the last level with it shown as complete rather
                // than being dropped onto a level that does not exist.
                return;
            }

            _progressionModel.CurrentLevelNumber.Value = nextLevelNumber;
            _saveData.currentLevelNumber = nextLevelNumber;
            Save();

            // A new level's cumulative objective starts at zero: "restore" would only ever find an
            // entry for it if this level had been reached before, and this mode has no way back onto
            // a level it has left.
            ApplyLevelObjective(nextLevelNumber, restoreSavedProgress: false);

            // Classic (GameMode.Timed) is played with every extra off — no power-ups can be used there —
            // so rolling onto the next level pays none either: a Row Clear earned for a four-line clear in
            // Classic would only pile up unusable.
            if (_gameModeSystem.ExtrasEnabled)
            {
                GrantLevelUpReward(completedLevelNumber);
            }

            _levelAdvancedPublisher.Publish(new LevelAdvancedMessage(nextLevelNumber));
        }

        /// <summary>
        /// Pays out the power-ups the player just earned for a first clear of
        /// <paramref name="completedLevelNumber"/>: every level pays at least one, a milestone pays a
        /// bundle — see <see cref="LevelCompletionRewards"/> for which kinds. Each is its own
        /// <see cref="PowerUpSystem.GrantDirect"/>, so a bundle of three publishes three grants and the
        /// fly-in plays three queued flights.
        /// <para>
        /// Only ever reached from a first clear: both callers advance the frontier first, and a Path
        /// replay of an already-cleared level never gets past
        /// <see cref="TryAdvanceFrontierAfterPathLevel"/>'s guard — so a replay pays nothing.
        /// </para>
        /// <para>
        /// The reward belongs to the level that was <em>completed</em>, not the one arrived at: it is
        /// payment for work done, so which level pays is decided by what the player cleared rather than
        /// by where they landed. That also keeps the last level honest — completing it advances
        /// nowhere, so this is never reached from there and a level that cannot be left cannot pay
        /// repeatedly either.
        /// </para>
        /// <para>
        /// Goes through <see cref="PowerUpSystem.GrantDirect"/> rather than the rewarded-ad path, for
        /// the same reason a badge does: the level is already cleared, so there is nothing left to gate
        /// on and a declined ad must not be able to swallow a reward the player has earned.
        /// </para>
        /// </summary>
        private void GrantLevelUpReward(int completedLevelNumber)
        {
            IReadOnlyList<PowerUpKind> rewards = LevelCompletionRewards.For(completedLevelNumber);
            for (int rewardIndex = 0; rewardIndex < rewards.Count; rewardIndex++)
            {
                _powerUpSystem.GrantDirect(rewards[rewardIndex]);
            }
        }

        /// <summary>
        /// Persists cumulative progress as it moves. Per-run progress is deliberately not written: it
        /// resets on game over by design, so saving it would resurrect progress the player lost.
        /// <para>
        /// Never writes while in Path mode. A Path run always starts its objective fresh regardless of
        /// scope (see <see cref="TryStartPathLevel"/>), so its progress values are relative to that
        /// bounded attempt, not the Endless/Timed cumulative tally this save entry represents — writing
        /// them here would silently overwrite (and could regress) whatever real cumulative progress the
        /// player earned through Endless/Timed play of this level.
        /// </para>
        /// </summary>
        private void OnObjectiveProgressChanged(ObjectiveProgressChangedMessage message)
        {
            if (_gameModeSystem.CurrentMode.Value == GameMode.Path)
            {
                return;
            }

            // Scanned across every tracked objective, not just the primary one: each of a level's
            // objectives keeps its own save entry under its own id.
            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < tracked.Count; objectiveIndex++)
            {
                ObjectiveProgress objective = tracked[objectiveIndex];
                if (objective.Definition.Id != message.ObjectiveId
                    || objective.Definition.Scope != ObjectiveScope.Cumulative)
                {
                    continue;
                }

                WriteCumulativeProgress(message.ObjectiveId, message.CurrentValue);
                Save();
                return;
            }
        }

        /// <summary>
        /// Whether <paramref name="objectiveId"/> is one of the objectives the level currently being
        /// played asks for. Membership of the whole tracked set rather than a match against the primary
        /// one, now that a level may ask for several at once — checking only the first would make a
        /// second objective's completion look exactly like a stale message from a previous level.
        /// </summary>
        private bool IsTrackedObjective(string objectiveId)
        {
            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < tracked.Count; objectiveIndex++)
            {
                if (tracked[objectiveIndex].Definition.Id == objectiveId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether every objective the current level asks for has been cleared — the AND that decides
        /// when a multi-objective level is done.
        /// <para>
        /// An objective counts as cleared if its own progress says so <em>or</em> if its completion
        /// message has already been seen this level. The second half matters because completion is
        /// announced by <see cref="ObjectiveSystem"/> on the false-to-true edge and this system is one
        /// of several subscribers: it must not depend on having been notified after the model was
        /// written, and it must not care which subscriber ran first.
        /// </para>
        /// </summary>
        private bool AllTrackedObjectivesComplete()
        {
            IReadOnlyList<ObjectiveProgress> tracked = _objectiveModel.TrackedObjectives;
            if (tracked.Count == 0)
            {
                return false;
            }

            for (int objectiveIndex = 0; objectiveIndex < tracked.Count; objectiveIndex++)
            {
                ObjectiveProgress objective = tracked[objectiveIndex];
                if (!objective.IsComplete && !_completedObjectiveIds.Contains(objective.Definition.Id))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds every objective <paramref name="levelNumber"/> is authored with — a level may ask for
        /// several at once — and hands them to <see cref="ObjectiveModel"/> as the tracked set. An
        /// unauthored level, or one whose every row is invalid, clears the tracking instead of throwing:
        /// a broken content row must not take the scene down.
        /// <para>
        /// Takes the level as an argument rather than reading the frontier, because in Path mode the
        /// level being played and the frontier are two different numbers.
        /// </para>
        /// </summary>
        private void ApplyLevelObjective(int levelNumber, bool restoreSavedProgress)
        {
            // Every level change starts the completion bookkeeping over: ids from the level being left
            // must not count towards the new one's AND, and a replayed level has to be cleared again.
            _completedObjectiveIds.Clear();

            IReadOnlyList<LevelObjectiveConfig> configs = _levelCatalog.FindAll(levelNumber);
            if (configs.Count == 0)
            {
                Debug.LogError(
                    $"{nameof(LevelCatalog)} has no level {levelNumber}. No objective will be shown until "
                    + "the catalog is fixed.");
                _objectiveModel.SetObjectives(null);
                return;
            }

            _objectiveBuffer.Clear();

            for (int configIndex = 0; configIndex < configs.Count; configIndex++)
            {
                LevelObjectiveConfig config = configs[configIndex];
                if (!config.IsValid(out string error))
                {
                    // One bad row is skipped rather than taking the whole level down with it: the
                    // level's other objectives are still playable, and a level left with none falls
                    // through to the same cleared tracking an unauthored one gets.
                    Debug.LogError($"Level {levelNumber} is misconfigured and was skipped: {error}");
                    continue;
                }

                // The row's position among this level's rows is what keeps the generated ids unique —
                // see LevelObjectiveConfig.ToObjectiveDefinition.
                var progress = new ObjectiveProgress(config.ToObjectiveDefinition(configIndex));

                if (restoreSavedProgress && config.Scope == ObjectiveScope.Cumulative)
                {
                    progress.RestoreProgress(ReadCumulativeProgress(progress.Definition.Id));
                }

                _objectiveBuffer.Add(progress);
            }

            _objectiveModel.SetObjectives(_objectiveBuffer);
        }

        /// <summary>Keeps a saved level number inside what the catalog actually authors, so shrinking
        /// the catalog cannot strand a player on a level that no longer exists.</summary>
        private int ClampToCatalog(int levelNumber)
        {
            int maxLevelNumber = _levelCatalog.MaxLevelNumber;
            if (maxLevelNumber <= 0)
            {
                return FIRST_LEVEL_NUMBER;
            }

            return Mathf.Clamp(levelNumber, FIRST_LEVEL_NUMBER, maxLevelNumber);
        }

        private int ReadCumulativeProgress(string objectiveId)
        {
            for (int entryIndex = 0; entryIndex < _saveData.cumulativeProgress.Count; entryIndex++)
            {
                LevelProgressSaveData.CumulativeProgressEntry entry = _saveData.cumulativeProgress[entryIndex];
                if (entry != null && entry.objectiveId == objectiveId)
                {
                    return entry.currentValue;
                }
            }

            return 0;
        }

        private void WriteCumulativeProgress(string objectiveId, int currentValue)
        {
            for (int entryIndex = 0; entryIndex < _saveData.cumulativeProgress.Count; entryIndex++)
            {
                LevelProgressSaveData.CumulativeProgressEntry entry = _saveData.cumulativeProgress[entryIndex];
                if (entry != null && entry.objectiveId == objectiveId)
                {
                    entry.currentValue = currentValue;
                    return;
                }
            }

            _saveData.cumulativeProgress.Add(new LevelProgressSaveData.CumulativeProgressEntry
            {
                objectiveId = objectiveId,
                currentValue = currentValue,
            });
        }

        /// <summary>
        /// Reads the save blob, falling back to a fresh one on anything unreadable. Corrupt save data
        /// costs the player their progress either way; crashing the boot on top of that does not help.
        /// </summary>
        private static LevelProgressSaveData Load()
        {
            string json = PlayerPrefs.GetString(SAVE_KEY, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new LevelProgressSaveData();
            }

            LevelProgressSaveData data = null;
            try
            {
                data = JsonUtility.FromJson<LevelProgressSaveData>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"Level progress save data was unreadable and has been reset: {exception.Message}");
            }

            if (data == null)
            {
                return new LevelProgressSaveData();
            }

            // JsonUtility leaves a field absent from the JSON at its declared default, so an older
            // blob written before a field existed still deserializes; only the list needs guarding
            // because JsonUtility writes null for one that was never populated.
            if (data.cumulativeProgress == null)
            {
                data.cumulativeProgress = new System.Collections.Generic.List<LevelProgressSaveData.CumulativeProgressEntry>();
            }

            data.currentLevelNumber = Math.Max(FIRST_LEVEL_NUMBER, data.currentLevelNumber);
            return data;
        }

        private void Save()
        {
            _saveData.schemaVersion = LevelProgressSaveData.CURRENT_SCHEMA_VERSION;
            PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(_saveData));
        }
    }
}
