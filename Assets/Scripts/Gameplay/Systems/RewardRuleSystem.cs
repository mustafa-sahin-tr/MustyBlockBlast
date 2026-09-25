using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Pays the rule-based power-up rewards (issue #464): tracks the player's streak of Path levels
    /// cleared on the first attempt and, each time it lands on a multiple of an authored rule's
    /// threshold, grants that rule's bonus on top of the level's own reward.
    /// <para>
    /// <b>What extends the streak.</b> A first clear of the frontier level in <see cref="GameMode.Path"/>
    /// — read off <see cref="LevelAdvancedMessage"/>, which <see cref="LevelProgressionSystem"/>
    /// publishes only when a clear moves the frontier, after the level's own reward and before the
    /// run ends. So the bonus lands in the same frame as that reward and is already on
    /// <see cref="RewardRuleModel"/> when the result card opens. A clear that follows a failed attempt
    /// at the same level is not a first try: it leaves the streak at zero.
    /// </para>
    /// <para>
    /// <b>What breaks it.</b> Only a failed Path run of the frontier level — any ending other than
    /// <see cref="GameOverReason.LevelCompleted"/>. A failure the no-moves rescue then takes back
    /// (<see cref="RunRescuedMessage"/>) is undone, so a rescued run can still be a first try. Undo,
    /// restarting or leaving mid-level do not end a run as a failure and so never touch the streak.
    /// </para>
    /// <para>
    /// <b>What is ignored.</b> Replays of already-cleared levels (neither advance nor break), and
    /// Endless/Timed play entirely — the streak is a Path-mode idea.
    /// </para>
    /// </summary>
    public sealed class RewardRuleSystem : IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole JSON save blob — see <see cref="RewardRuleSaveData"/>.</summary>
        private const string SAVE_KEY = "RewardRules.Progress";

        private readonly RewardRuleModel _model;
        private readonly RewardRuleCatalog _catalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly GameModeModel _gameModeModel;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly IDisposable _subscriptions;
        private readonly RewardRuleSaveData _saveData;

        /// <summary>Reused per payout so granting a bonus does not allocate a list each time.</summary>
        private readonly List<PowerUpKind> _payoutBuffer = new List<PowerUpKind>(4);

        /// <summary>
        /// Whether the failure most recently recorded can still be taken back by a rescue — true from a
        /// rescue-available failure until the rescue lands or the next run starts. While it is, the
        /// pre-failure state is held in the two fields below.
        /// </summary>
        private bool _hasRescueSnapshot;
        private int _snapshotStreak;
        private int _snapshotFailedLevelNumber;

        public RewardRuleSystem(
            RewardRuleModel model,
            RewardRuleCatalog catalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            GameModeModel gameModeModel,
            PowerUpSystem powerUpSystem,
            ISubscriber<LevelAdvancedMessage> levelAdvancedSubscriber,
            ISubscriber<GameOverMessage> gameOverSubscriber,
            ISubscriber<RunRescuedMessage> runRescuedSubscriber,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _model = model;
            _catalog = catalog;
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
            _gameModeModel = gameModeModel;
            _powerUpSystem = powerUpSystem;

            _saveData = Load();
            _model.FirstTryStreak.Value = _saveData.firstTryStreak;

            DisposableBagBuilder bag = DisposableBag.CreateBuilder();
            levelAdvancedSubscriber.Subscribe(OnLevelAdvanced).AddTo(bag);
            gameOverSubscriber.Subscribe(OnGameOver).AddTo(bag);
            runRescuedSubscriber.Subscribe(OnRunRescued).AddTo(bag);
            runStartedSubscriber.Subscribe(OnRunStarted).AddTo(bag);
            _subscriptions = bag.Build();
        }

        public void Dispose() => _subscriptions.Dispose();

        private bool IsPathMode => _gameModeModel.CurrentMode.Value == GameMode.Path;

        /// <summary>A new run closes the rescue window on the last failure and starts a fresh result
        /// card, so neither the snapshot nor the last payout may carry into it.</summary>
        private void OnRunStarted(RunStartedMessage message)
        {
            _hasRescueSnapshot = false;
            _model.ClearPayout();
        }

        /// <summary>
        /// A first clear of <c>CurrentLevelNumber − 1</c> just moved the frontier. Extends the streak
        /// (or, after a failed attempt at that level, leaves it at zero), persists it, and pays every
        /// rule the new value lands on.
        /// </summary>
        private void OnLevelAdvanced(LevelAdvancedMessage message)
        {
            if (!IsPathMode)
            {
                return;
            }

            int completedLevelNumber = message.CurrentLevelNumber - 1;
            bool wasFirstTry = _saveData.failedLevelNumber != completedLevelNumber;

            _saveData.failedLevelNumber = 0;
            SetStreak(wasFirstTry ? _saveData.firstTryStreak + 1 : 0);
            Save();

            if (wasFirstTry)
            {
                PayRules(completedLevelNumber);
            }
        }

        /// <summary>
        /// Whether a clear of <paramref name="levelNumber"/> would count as a first try: it is the
        /// frontier and has not been failed since the last clear. Read by the level-start card to
        /// preview the streak bonus.
        /// </summary>
        public bool IsFirstTryPending(int levelNumber)
            => levelNumber == _progressionModel.CurrentLevelNumber.Value && _saveData.failedLevelNumber != levelNumber;

        /// <summary>
        /// A Path run of the frontier level failed: the streak drops to zero and the level is marked as
        /// failed, so its eventual clear is not a first try. Committed and saved at once — waiting for
        /// the next run would let an app kill on the game-over screen dodge the reset — with a snapshot
        /// kept while the ending can still be rescued.
        /// </summary>
        private void OnGameOver(GameOverMessage message)
        {
            if (!IsPathMode || message.Reason == GameOverReason.LevelCompleted)
            {
                return;
            }

            int failedLevelNumber = _pathRunModel.ActiveLevelNumber.Value;
            if (failedLevelNumber != _progressionModel.CurrentLevelNumber.Value)
            {
                // A replay of an already-cleared level: neither extends nor breaks the streak.
                return;
            }

            // Taken only once per rescue window: a rescued run that dead-ends again is still the same
            // attempt, and snapshotting the second time would capture the already-reset zero.
            if (message.IsRescueAvailable && !_hasRescueSnapshot)
            {
                _hasRescueSnapshot = true;
                _snapshotStreak = _saveData.firstTryStreak;
                _snapshotFailedLevelNumber = _saveData.failedLevelNumber;
            }

            _saveData.failedLevelNumber = failedLevelNumber;
            SetStreak(0);
            Save();
        }

        /// <summary>The failure just recorded was taken back by the rescue ad: the run goes on, so the
        /// attempt it belongs to may still be a first try.</summary>
        private void OnRunRescued(RunRescuedMessage message)
        {
            if (!_hasRescueSnapshot)
            {
                return;
            }

            _hasRescueSnapshot = false;
            _saveData.failedLevelNumber = _snapshotFailedLevelNumber;
            SetStreak(_snapshotStreak);
            Save();
        }

        /// <summary>
        /// Grants every rule that pays at the current streak, in catalog order, through
        /// <see cref="PowerUpSystem.GrantDirect"/> — the same path the level's own reward takes, so each
        /// bonus is persisted, announced and flown to the strip exactly like it.
        /// </summary>
        private void PayRules(int completedLevelNumber)
        {
            _payoutBuffer.Clear();
            RewardRules.AppendFirstTryPayout(_catalog.Rules, _saveData.firstTryStreak, completedLevelNumber, _payoutBuffer);

            if (_payoutBuffer.Count == 0)
            {
                return;
            }

            for (int payoutIndex = 0; payoutIndex < _payoutBuffer.Count; payoutIndex++)
            {
                _powerUpSystem.GrantDirect(_payoutBuffer[payoutIndex], PowerUpGrantSource.StreakBonus);
            }

            _model.RecordPayout(completedLevelNumber, _payoutBuffer);
        }

        private void SetStreak(int streak)
        {
            _saveData.firstTryStreak = streak;
            _model.FirstTryStreak.Value = streak;
        }

        /// <summary>Reads the save blob, falling back to a fresh one on anything unreadable — a lost
        /// streak is a small price, a crashed boot is not.</summary>
        private static RewardRuleSaveData Load()
        {
            string json = PlayerPrefs.GetString(SAVE_KEY, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new RewardRuleSaveData();
            }

            RewardRuleSaveData data = null;
            try
            {
                data = JsonUtility.FromJson<RewardRuleSaveData>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"Reward rule save data was unreadable and has been reset: {exception.Message}");
            }

            if (data == null)
            {
                return new RewardRuleSaveData();
            }

            data.firstTryStreak = Math.Max(0, data.firstTryStreak);
            return data;
        }

        private void Save()
        {
            _saveData.schemaVersion = RewardRuleSaveData.CURRENT_SCHEMA_VERSION;
            PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(_saveData));
        }
    }
}
