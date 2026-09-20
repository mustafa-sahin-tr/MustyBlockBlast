using System;
using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="BadgeModel"/>: builds the tracked badge set from <see cref="BadgeCatalog"/>,
    /// watches <see cref="BadgeStatsModel"/>'s lifetime counters, latches a badge unlocked the moment a
    /// threshold is crossed, and pays its coin reward when the player later claims it.
    /// <para>
    /// Unlocking and claiming are two steps on purpose. A badge falls mid-run, when the player is
    /// looking at the board, so the unlock only latches state and persists it; nothing is credited.
    /// The coins are paid by <see cref="ClaimReward"/>, which a tap on the badge's tile reaches, and
    /// they go through <see cref="CurrencySystem"/> because that class is the one and only writer of
    /// the coin balance — this one never touches <see cref="ProfileModel"/> directly.
    /// </para>
    /// <para>
    /// Each unlock is also announced as a <see cref="BadgeUnlockedMessage"/> and remembered in
    /// <see cref="BadgeModel.UnlockedThisRun"/> until the next <see cref="RunStartedMessage"/>, which
    /// is what lets the end-of-run result screen list exactly this run's badges (issue #221).
    /// </para>
    /// <para>
    /// Paying exactly once is structural rather than checked: <see cref="BadgeProgress.Evaluate"/> is
    /// a one-way latch that only returns true on the false-to-true transition, a badge restored from
    /// the save file starts latched, and the claim latch in the model is one-way too. Neither a
    /// repeated counter update, a double tap nor an app relaunch can pay twice.
    /// </para>
    /// <para>
    /// Persistence ordering, in case of a force-quit. The unlock is written and flushed before any
    /// claim is possible, so a claimable badge is never lost. A claim credits and flushes the coins
    /// first, then writes and flushes the claimed id. The one window that a crash can fall into is
    /// between those two flushes, and it errs in the player's favour: they may claim once more, but
    /// can never be left with a claimed badge that was not paid.
    /// </para>
    /// </summary>
    public sealed class BadgeSystem : IDisposable
    {
        /// <summary>Single PlayerPrefs key holding the whole JSON save blob — see <see cref="BadgeSaveData"/>.</summary>
        private const string SAVE_KEY = "Badges.Unlocked";

        private readonly BadgeModel _badgeModel;
        private readonly CurrencySystem _currencySystem;
        private readonly IPublisher<BadgeUnlockedMessage> _unlockedPublisher;
        private readonly BadgeSaveData _saveData;
        private readonly CompositeDisposable _disposables = new CompositeDisposable();
        private readonly IDisposable _runStartedSubscription;

        /// <summary>Coin reward per tracked badge, index-aligned with <see cref="BadgeModel.Badges"/>.
        /// Held here so a claim does not have to scan the catalog for its own payout.</summary>
        private readonly List<int> _coinRewards = new List<int>();

        /// <summary>Ids latched by the counter move being handled, reused across calls so the hot
        /// placement path allocates nothing. Only ever non-empty inside <see cref="OnStatChanged"/>.</summary>
        private readonly List<string> _justUnlocked = new List<string>();

        public BadgeSystem(
            BadgeModel badgeModel,
            BadgeStatsModel statsModel,
            BadgeCatalog badgeCatalog,
            CurrencySystem currencySystem,
            IPublisher<BadgeUnlockedMessage> unlockedPublisher,
            ISubscriber<RunStartedMessage> runStartedSubscriber)
        {
            _badgeModel = badgeModel;
            _currencySystem = currencySystem;
            _unlockedPublisher = unlockedPublisher;
            _saveData = Load();

            BuildBadges(badgeCatalog);

            // A run's unlock list belongs to that run alone. Cleared on the start of the next one
            // rather than on game over, because the result screen reads it *after* game over.
            _runStartedSubscription = runStartedSubscriber.Subscribe(OnRunStarted);

            // Subscribing last, and only after the save file has re-latched every already-unlocked
            // badge: ReactiveProperty.Subscribe fires immediately with the current value, so this is
            // also the boot-time evaluation. Without the restore above, every badge the player had
            // already earned would become claimable again on every launch.
            Watch(statsModel.TotalPiecesPlaced, BadgeStatType.TotalPiecesPlaced);
            Watch(statsModel.TotalLinesCleared, BadgeStatType.TotalLinesCleared);
            Watch(statsModel.TotalBoardWipes, BadgeStatType.TotalBoardWipes);
            Watch(statsModel.HighestScoreEver, BadgeStatType.HighestScoreEver);
            Watch(statsModel.TotalRunsPlayed, BadgeStatType.TotalRunsPlayed);
            Watch(statsModel.TotalPowerUpsApplied, BadgeStatType.TotalPowerUpsApplied);
        }

        public void Dispose()
        {
            _disposables.Dispose();
            _runStartedSubscription.Dispose();
        }

        /// <summary>
        /// Coins <paramref name="badgeId"/> pays when claimed, or zero for an unknown badge. Read by
        /// the badge wall so the amount on the tile and the amount credited are one number.
        /// </summary>
        public int CoinRewardOf(string badgeId)
        {
            int badgeIndex = IndexOf(badgeId);
            return badgeIndex < 0 ? 0 : _coinRewards[badgeIndex];
        }

        /// <summary>
        /// Whether a tap on <paramref name="badgeId"/> would pay out: unlocked, not yet claimed, and
        /// worth something. The one definition of "claimable", shared by the tile that draws the
        /// indicator and the claim below that honours it.
        /// </summary>
        public bool IsClaimable(string badgeId)
        {
            int badgeIndex = IndexOf(badgeId);
            return badgeIndex >= 0 && IsClaimableAt(badgeIndex);
        }

        /// <summary>
        /// Pays the coin reward of <paramref name="badgeId"/> and latches it claimed. Returns whether
        /// anything was paid. Refused outright — no balance change, no PlayerPrefs write — for a badge
        /// that is locked, already claimed, worth zero coins, or not in the catalog at all. A second tap
        /// on the same tile therefore does nothing, however quickly it follows the first.
        /// </summary>
        public bool ClaimReward(string badgeId)
        {
            int badgeIndex = IndexOf(badgeId);
            if (badgeIndex < 0 || !IsClaimableAt(badgeIndex))
            {
                return false;
            }

            // Coins first, and flushed inside the call: if the app dies between here and the claim
            // write below, the player keeps the coins and may claim once more — the recoverable half
            // of the pair. The other order would risk a claimed badge that was never paid.
            _currencySystem.CreditBadgeReward(_coinRewards[badgeIndex]);

            _badgeModel.MarkClaimed(badgeId);
            _saveData.claimedBadgeIds.Add(badgeId);
            Save();

            _badgeModel.Revision.Value += 1;
            return true;
        }

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
                if (Contains(_saveData.unlockedBadgeIds, config.Id))
                {
                    progress.RestoreUnlocked();
                }

                if (Contains(_saveData.claimedBadgeIds, config.Id))
                {
                    _badgeModel.MarkClaimed(config.Id);
                }

                progresses.Add(progress);
                _coinRewards.Add(config.CoinReward);
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
            _justUnlocked.Clear();

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

                _saveData.unlockedBadgeIds.Add(progress.Definition.Id);
                _badgeModel.AddUnlockedThisRun(progress.Definition.Id);
                _justUnlocked.Add(progress.Definition.Id);
            }

            if (_justUnlocked.Count == 0)
            {
                return;
            }

            // One write however many badges fell at once, and none at all on the overwhelmingly common
            // "counter moved, nothing unlocked" path. Flushed here so the unlock is on the disk before
            // the tile that lets the player claim it can even be drawn — and before it is announced,
            // so no listener can act on an unlock the disk does not know about yet.
            Save();
            _badgeModel.Revision.Value += 1;

            for (int unlockedIndex = 0; unlockedIndex < _justUnlocked.Count; unlockedIndex++)
            {
                _unlockedPublisher.Publish(new BadgeUnlockedMessage(_justUnlocked[unlockedIndex]));
            }

            _justUnlocked.Clear();
        }

        private void OnRunStarted(RunStartedMessage message) => _badgeModel.ClearUnlockedThisRun();

        private bool IsClaimableAt(int badgeIndex)
        {
            BadgeProgress progress = _badgeModel.Badges[badgeIndex];
            return progress.IsUnlocked
                && !_badgeModel.IsClaimed(progress.Definition.Id)
                && _coinRewards[badgeIndex] > 0;
        }

        private int IndexOf(string badgeId)
        {
            if (string.IsNullOrEmpty(badgeId))
            {
                return -1;
            }

            IReadOnlyList<BadgeProgress> badges = _badgeModel.Badges;
            for (int badgeIndex = 0; badgeIndex < badges.Count; badgeIndex++)
            {
                if (badges[badgeIndex].Definition.Id == badgeId)
                {
                    return badgeIndex;
                }
            }

            return -1;
        }

        private static bool Contains(List<string> ids, string badgeId)
        {
            for (int idIndex = 0; idIndex < ids.Count; idIndex++)
            {
                if (ids[idIndex] == badgeId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads the save blob, falling back to a fresh one on anything unreadable. The cost of a
        /// corrupt file here is re-offering rewards the player already had, which is strictly better
        /// than failing the boot over a cosmetic wall of badges.
        /// <para>
        /// Migrates a version-1 blob (unlocks only, paid in power-ups at the time) by seeding the
        /// claimed list from the unlocked one, exactly once, and writing the result back straight away
        /// so the next launch reads a version-2 blob and never runs this again. Without it every badge
        /// the player already held would light up as claimable on first launch after the update.
        /// </para>
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

            // JsonUtility writes null for a list that was never populated, so the lists are the fields
            // that need guarding before anything reads them.
            if (data.unlockedBadgeIds == null)
            {
                data.unlockedBadgeIds = new List<string>();
            }

            bool needsMigration = data.schemaVersion < 2 || data.claimedBadgeIds == null;
            if (data.claimedBadgeIds == null)
            {
                data.claimedBadgeIds = new List<string>();
            }

            if (needsMigration)
            {
                data.claimedBadgeIds.Clear();
                data.claimedBadgeIds.AddRange(data.unlockedBadgeIds);
                data.schemaVersion = BadgeSaveData.CURRENT_SCHEMA_VERSION;
                PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(data));
                PlayerPrefs.Save();
            }

            return data;
        }

        /// <summary>Writes and flushes the blob. Flushed every time, unlike the flat counters other
        /// Systems keep: each write here is a latch the player would notice losing.</summary>
        private void Save()
        {
            _saveData.schemaVersion = BadgeSaveData.CURRENT_SCHEMA_VERSION;
            PlayerPrefs.SetString(SAVE_KEY, JsonUtility.ToJson(_saveData));
            PlayerPrefs.Save();
        }
    }
}
