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
    /// </summary>
    public sealed class LevelProgressionSystem : IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole JSON save blob — see <see cref="LevelProgressSaveData"/>.</summary>
        private const string SAVE_KEY = "Levels.Progress";

        private const int FIRST_LEVEL_NUMBER = 1;

        private readonly LevelProgressionModel _progressionModel;
        private readonly ObjectiveModel _objectiveModel;
        private readonly LevelCatalog _levelCatalog;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly IPublisher<LevelAdvancedMessage> _levelAdvancedPublisher;
        private readonly IDisposable _subscriptions;
        private readonly LevelProgressSaveData _saveData;

        public LevelProgressionSystem(
            LevelProgressionModel progressionModel,
            ObjectiveModel objectiveModel,
            LevelCatalog levelCatalog,
            PowerUpSystem powerUpSystem,
            ISubscriber<ObjectiveCompletedMessage> objectiveCompletedSubscriber,
            ISubscriber<ObjectiveProgressChangedMessage> objectiveProgressChangedSubscriber,
            IPublisher<LevelAdvancedMessage> levelAdvancedPublisher)
        {
            _progressionModel = progressionModel;
            _objectiveModel = objectiveModel;
            _levelCatalog = levelCatalog;
            _powerUpSystem = powerUpSystem;
            _levelAdvancedPublisher = levelAdvancedPublisher;

            _saveData = Load();
            _progressionModel.CurrentLevelNumber.Value = ClampToCatalog(_saveData.currentLevelNumber);

            // Restores the saved value for a cumulative objective. Silent by design: nothing happened
            // this session, so no progress/completion message is published for the rehydration.
            ApplyCurrentLevelObjective(restoreSavedProgress: true);

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            objectiveCompletedSubscriber.Subscribe(OnObjectiveCompleted).AddTo(bag);
            objectiveProgressChangedSubscriber.Subscribe(OnObjectiveProgressChanged).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        /// <summary>
        /// Advancing is driven by the completion message rather than polled, so the level moves on in
        /// the same frame the qualifying placement resolved. Ignores completions belonging to anything
        /// other than the current level's objective, which is what makes a stale message from a
        /// previous level harmless.
        /// </summary>
        private void OnObjectiveCompleted(ObjectiveCompletedMessage message)
        {
            if (!IsCurrentLevelObjective(message.ObjectiveId))
            {
                return;
            }

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
            // entry for it if this level had been reached before, and re-entering a level is not a
            // thing this slice can do.
            ApplyCurrentLevelObjective(restoreSavedProgress: false);

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
        /// </summary>
        private void OnObjectiveProgressChanged(ObjectiveProgressChangedMessage message)
        {
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
        /// Builds the current level's objective from the catalog and hands it to
        /// <see cref="ObjectiveModel"/> as the one tracked objective. An unauthored or invalid level
        /// clears the tracking instead of throwing: a broken content row must not take the scene down.
        /// </summary>
        private void ApplyCurrentLevelObjective(bool restoreSavedProgress)
        {
            int levelNumber = _progressionModel.CurrentLevelNumber.Value;
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
