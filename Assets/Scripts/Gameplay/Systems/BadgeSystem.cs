using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="BadgeModel"/>: builds the tracked badge set from <see cref="BadgeCatalog"/>,
    /// watches <see cref="BadgeStatsModel"/>'s lifetime counters, and pays out a power-up the moment a
    /// threshold is crossed.
    /// <para>
    /// The payout goes through <see cref="PowerUpSystem.GrantDirect"/> rather than
    /// <c>GrantRewardAsync</c>: a badge is not ad-gated, it is already earned by the time it unlocks,
    /// so there is nothing left to ask the player for. Both methods share one grant helper inside
    /// <see cref="PowerUpSystem"/>, so a badge grant mutates, persists and announces the inventory
    /// exactly as a rewarded one does.
    /// </para>
    /// <para>
    /// Paying exactly once is structural rather than checked: <see cref="BadgeProgress.Evaluate"/> is
    /// a one-way latch that only returns true on the false-to-true transition, and a badge restored
    /// from the save file starts latched, so neither a repeated counter update nor an app relaunch can
    /// grant twice.
    /// </para>
    /// </summary>
    public sealed class BadgeSystem : IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole JSON save blob — see <see cref="BadgeSaveData"/>.</summary>
        private const string SAVE_KEY = "Badges.Unlocked";

        private readonly BadgeModel _badgeModel;
        private readonly PowerUpSystem _powerUpSystem;
        private readonly BadgeSaveData _saveData;
        private readonly CompositeDisposable _disposables = new CompositeDisposable();

        /// <summary>Reward per tracked badge, index-aligned with <see cref="BadgeModel.Badges"/>. Held
        /// here so an unlock does not have to scan the catalog for its own payout.</summary>
        private readonly List<PowerUpKind> _rewards = new List<PowerUpKind>();

        public BadgeSystem(
            BadgeModel badgeModel,
            BadgeStatsModel statsModel,
            BadgeCatalog badgeCatalog,
            PowerUpSystem powerUpSystem)
        {
            _badgeModel = badgeModel;
            _powerUpSystem = powerUpSystem;
            _saveData = Load();

            BuildBadges(badgeCatalog);

            // Subscribing last, and only after the save file has re-latched every already-unlocked
            // badge: ReactiveProperty.Subscribe fires immediately with the current value, so this is
            // also the boot-time evaluation. Without the restore above, every badge the player had
            // already earned would pay out again on every launch.
            Watch(statsModel.TotalPiecesPlaced, BadgeStatType.TotalPiecesPlaced);
            Watch(statsModel.TotalLinesCleared, BadgeStatType.TotalLinesCleared);
            Watch(statsModel.TotalBoardWipes, BadgeStatType.TotalBoardWipes);
            Watch(statsModel.HighestScoreEver, BadgeStatType.HighestScoreEver);
            Watch(statsModel.TotalRunsPlayed, BadgeStatType.TotalRunsPlayed);
            Watch(statsModel.TotalPowerUpsApplied, BadgeStatType.TotalPowerUpsApplied);
        }

        public void Dispose() => _disposables.Dispose();

        /// <summary>
        /// Turns the authored catalog into tracked progress. An invalid row is skipped with one
        /// readable error rather than throwing: a mistyped threshold must cost the player that badge,
        /// not the scene.
        /// </summary>
        private void BuildBadges(BadgeCatalog badgeCatalog)
        {
            IReadOnlyList<BadgeConfig> configs = badgeCatalog.Badges;
            var progresses = new List<BadgeProgress>(configs.Count);

            for (int configIndex = 0; configIndex < configs.Count; configIndex++)
            {
                BadgeConfig config = configs[configIndex];
                if (config == null)
                {
                    continue;
                }

                if (!config.IsValid(out string error))
                {
                    Debug.LogError($"Badge '{config.Id}' is misconfigured and was skipped: {error}");
                    continue;
                }

                var progress = new BadgeProgress(config.ToBadgeDefinition());
                if (IsPersistedUnlocked(config.Id))
                {
                    progress.RestoreUnlocked();
                }

                progresses.Add(progress);
                _rewards.Add(config.Reward);
            }

            _badgeModel.SetBadges(progresses);
        }

        private void Watch(ReactiveProperty<long> counter, BadgeStatType statType)
        {
            counter.Subscribe(value => OnStatChanged(statType, value)).AddTo(_disposables);
        }

        /// <summary>
        /// Re-evaluates every badge hanging off the counter that just moved. Only that counter's
        /// badges: nothing else can have changed, so a wide sweep would be pure waste on a path that
        /// runs on every single placement.
        /// </summary>
        private void OnStatChanged(BadgeStatType statType, long value)
        {
            IReadOnlyList<BadgeProgress> badges = _badgeModel.Badges;
            bool anyUnlocked = false;

            for (int badgeIndex = 0; badgeIndex < badges.Count; badgeIndex++)
            {
                BadgeProgress progress = badges[badgeIndex];
                if (progress.Definition.StatType != statType)
                {
                    continue;
                }

                if (!progress.Evaluate(value))
                {
                    continue;
                }

                _powerUpSystem.GrantDirect(_rewards[badgeIndex]);
                _saveData.unlockedBadgeIds.Add(progress.Definition.Id);
                anyUnlocked = true;
            }

            // One write however many badges fell at once, and none at all on the overwhelmingly common
            // "counter moved, nothing unlocked" path.
            if (anyUnlocked)
            {
                Save();
            }
        }

        private bool IsPersistedUnlocked(string badgeId)
        {
            for (int idIndex = 0; idIndex < _saveData.unlockedBadgeIds.Count; idIndex++)
            {
                if (_saveData.unlockedBadgeIds[idIndex] == badgeId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the save blob, falling back to a fresh one on anything unreadable. The cost of a
        /// corrupt file here is re-granting rewards the player already had, which is strictly better
        /// than failing the boot over a cosmetic wall of badges.
        /// </summary>
        private static BadgeSaveData Load()
        {
            string json = PlayerPrefs.GetString(SAVE_KEY, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new BadgeSaveData();
            }

            BadgeSaveData data = null;
            try
            {
                data = JsonUtility.FromJson<BadgeSaveData>(json);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError($"Badge save data was unreadable and has been reset: {exception.Message}");
            }

            if (data == null)
            {
                return new BadgeSaveData();
            }

            // JsonUtility writes null for a list that was never populated, so the list is the one
            // field that needs guarding before anything reads it.
            if (data.unlockedBadgeIds == null)
            {
                data.unlockedBadgeIds = new List<string>();
            }

            return data;
        }

        private void Save()
        {
            _saveData.schemaVersion = BadgeSaveData.CURRENT_SCHEMA_VERSION;
            PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(_saveData));
        }
    }
}
