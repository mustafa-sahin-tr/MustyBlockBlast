using System;
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
    /// It also pays out the level-up bonus power-up, because advancing is the event that earns it and
    /// this is the only place advancing happens. Which levels reward and with what is authored in the
    /// catalog rather than derived here — see <see cref="LevelObjectiveConfig.GrantsLevelUpReward"/>.
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

        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly ObjectiveModel _objectiveModel;
        private readonly LevelCatalog _levelCatalog;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly GameModeSystem _gameModeSystem;
        private readonly BoardSystem _boardSystem;
        private readonly ScoreSystem _scoreSystem;
        private readonly IPublisher<LevelAdvancedMessage> _levelAdvancedPublisher;
        private readonly IDisposable _subscriptions;
        private readonly LevelProgressSaveData _saveData;

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
            LevelCatalog levelCatalog,
            PowerUpSystem powerUpSystem,
            GameModeSystem gameModeSystem,
            BoardSystem boardSystem,
            ScoreSystem scoreSystem,
            ISubscriber<ObjectiveCompletedMessage> objectiveCompletedSubscriber,
            ISubscriber<ObjectiveProgressChangedMessage> objectiveProgressChangedSubscriber,
            IPublisher<LevelAdvancedMessage> levelAdvancedPublisher)
        {
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
            _objectiveModel = objectiveModel;
            _levelCatalog = levelCatalog;
            _powerUpSystem = powerUpSystem;
            _gameModeSystem = gameModeSystem;
            _boardSystem = boardSystem;
            _scoreSystem = scoreSystem;
            _levelAdvancedPublisher = levelAdvancedPublisher;

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

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>
        /// Starts a Path-mode run at <paramref name="levelNumber"/>, as the level path overlay's node
        /// tap does. Returns false — changing nothing at all — when the request is not one this mode
        /// accepts: outside <see cref="GameMode.Path"/>, for a level past the unlocked frontier, or for
        /// a level the catalog does not author.
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
            if (!IsCurrentLevelObjective(message.ObjectiveId))
            {
                return;
            }

            if (_gameModeSystem.CurrentMode.Value == GameMode.Path)
            {
                CompletePathLevel();
                return;
            }

            AdvanceToNextLevel();
        }

        /// <summary>
        /// Ends a Path-mode run as a success: pays the level's score bonus into the run that earned it,
        /// folds that run's finished score into the walk's running total, banks a first clear, and only
        /// then declares the run over.
        /// <para>
        /// The order matters. The bonus has to be inside <c>ScoreModel.Score</c> before
        /// <see cref="BoardSystem.ForceGameOver"/> publishes, because that score is what the end-of-run
        /// card reads and what the path tally sums — paying it afterwards would show the player a total
        /// that is short by exactly the reward they just earned.
        /// </para>
        /// </summary>
        private void CompletePathLevel()
        {
            int completedLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            LevelObjectiveConfig completedLevel = _levelCatalog.Find(completedLevelNumber);
            int bonus = completedLevel != null ? completedLevel.CompletionScoreBonus : 0;

            int finalScore = _scoreSystem.AddLevelCompletionBonus(bonus);
            _pathRunModel.RecordLevelCompletion(completedLevelNumber, finalScore);

            TryAdvanceFrontierAfterPathLevel(completedLevelNumber);

            _boardSystem.ForceGameOver(GameOverReason.LevelCompleted);
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

            GrantLevelUpReward(completedLevelNumber);

            _levelAdvancedPublisher.Publish(new LevelAdvancedMessage(nextLevelNumber));
        }

        /// <summary>
        /// Pays out the bonus power-up the player just earned, if the level they finished authored one.
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
            LevelObjectiveConfig completedLevel = _levelCatalog.Find(completedLevelNumber);
            if (completedLevel == null || !completedLevel.GrantsLevelUpReward)
            {
                return;
            }

            _powerUpSystem.GrantDirect(completedLevel.LevelUpReward);
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

            ObjectiveProgress current = _objectiveModel.CurrentObjective;
            if (current == null
                || current.Definition.Id != message.ObjectiveId
                || current.Definition.Scope != ObjectiveScope.Cumulative)
            {
                return;
            }

            WriteCumulativeProgress(message.ObjectiveId, message.CurrentValue);
            Save();
        }

        private bool IsCurrentLevelObjective(string objectiveId)
        {
            ObjectiveProgress current = _objectiveModel.CurrentObjective;
            return current != null && current.Definition.Id == objectiveId;
        }

        /// <summary>
        /// Builds <paramref name="levelNumber"/>'s objective from the catalog and hands it to
        /// <see cref="ObjectiveModel"/> as the one tracked objective. An unauthored or invalid level
        /// clears the tracking instead of throwing: a broken content row must not take the scene down.
        /// <para>
        /// Takes the level as an argument rather than reading the frontier, because in Path mode the
        /// level being played and the frontier are two different numbers.
        /// </para>
        /// </summary>
        private void ApplyLevelObjective(int levelNumber, bool restoreSavedProgress)
        {
            LevelObjectiveConfig config = _levelCatalog.Find(levelNumber);
            if (config == null)
            {
                Debug.LogError(
                    $"{nameof(LevelCatalog)} has no level {levelNumber}. No objective will be shown until "
                    + "the catalog is fixed.");
                _objectiveModel.SetCurrentObjective(null);
                return;
            }

            if (!config.IsValid(out string error))
            {
                Debug.LogError($"Level {levelNumber} is misconfigured and was skipped: {error}");
                _objectiveModel.SetCurrentObjective(null);
                return;
            }

            ObjectiveProgress progress = new ObjectiveProgress(config.ToObjectiveDefinition());

            if (restoreSavedProgress && config.Scope == ObjectiveScope.Cumulative)
            {
                progress.RestoreProgress(ReadCumulativeProgress(progress.Definition.Id));
            }

            _objectiveModel.SetCurrentObjective(progress);
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
