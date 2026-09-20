using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the badge unlock-then-claim flow from issue #214: an unlock latches and persists without
    /// paying, a tap-claim pays exactly once through <see cref="CurrencySystem"/>, refusals change
    /// nothing, and a pre-#214 save migrates with every held badge already claimed.
    /// <para>
    /// <see cref="CurrencySystem"/> is built for real rather than stubbed, because it is the one and
    /// only writer of the balance and the ordering of its flush against the badge save is the point.
    /// The construction is the same shape <c>CurrencySystemTests</c> uses.
    /// </para>
    /// </summary>
    public class BadgeSystemTests
    {
        private const string BADGE_SAVE_KEY = "Badges.Unlocked";
        private const string COIN_BALANCE_KEY = "Profile.CoinBalance";
        private const string TOTAL_SCORE_EARNED_KEY = "Profile.TotalScoreEarned";
        private const string SCORE_CONVERTED_KEY = "Profile.ScoreConverted";
        private const string CONSUMED_TRANSACTION_IDS_KEY = "Profile.ConsumedTransactionIds";

        private const string FIRST_STEPS = "first_steps";
        private const string LINE_CUTTER = "line_cutter";
        private const string TROPHY_ONLY = "trophy_only";

        private const int FIRST_STEPS_REWARD = 25;
        private const int LINE_CUTTER_REWARD = 50;

        /// <summary>
        /// Three badges: two paying, one worth nothing. Authored through the serializer, since
        /// <see cref="BadgeConfig"/>'s fields are Inspector-only by design.
        /// </summary>
        private const string CATALOG_JSON =
            "{\"_badges\":["
            + "{\"_id\":\"" + FIRST_STEPS + "\",\"_displayName\":\"First Steps\",\"_displayNameKey\":\"\","
            + "\"_statType\":0,\"_threshold\":50,\"_coinReward\":25},"
            + "{\"_id\":\"" + LINE_CUTTER + "\",\"_displayName\":\"Line Cutter\",\"_displayNameKey\":\"\","
            + "\"_statType\":1,\"_threshold\":100,\"_coinReward\":50},"
            + "{\"_id\":\"" + TROPHY_ONLY + "\",\"_displayName\":\"Trophy\",\"_displayNameKey\":\"\","
            + "\"_statType\":2,\"_threshold\":5,\"_coinReward\":0}"
            + "]}";

        private BadgeCatalog _catalog;
        private BadgeModel _badgeModel;
        private BadgeStatsModel _statsModel;
        private ProfileModel _profileModel;
        private TestMessageBroker<BadgeUnlockedMessage> _unlockedBroker;
        private TestMessageBroker<RunStartedMessage> _runStartedBroker;
        private CurrencySystem _currencySystem;
        private PowerUpSystem _powerUpSystem;
        private BadgeSystem _badgeSystem;

        [SetUp]
        public void ClearPersistedState()
        {
            DeleteKeys();
            _catalog = ScriptableObject.CreateInstance<BadgeCatalog>();
            JsonUtility.FromJsonOverwrite(CATALOG_JSON, _catalog);
        }

        [TearDown]
        public void ClearPersistedStateAfterwards()
        {
            _badgeSystem?.Dispose();
            _currencySystem?.Dispose();
            _powerUpSystem?.Dispose();
            DeleteKeys();
            if (_catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(_catalog);
            }
        }

        [Test]
        public void Unlock_LatchesAndPersistsWithoutPayingCoins()
        {
            BuildSystems();

            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.IsTrue(_badgeModel.Badges[0].IsUnlocked);
            Assert.IsTrue(_badgeSystem.IsClaimable(FIRST_STEPS));
            Assert.AreEqual(0, _profileModel.CoinBalance.Value);
            Assert.AreEqual(0, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            StringAssert.Contains(FIRST_STEPS, PlayerPrefs.GetString(BADGE_SAVE_KEY));
        }

        [Test]
        public void Unlock_BumpsTheModelRevision()
        {
            BuildSystems();
            int revisionBefore = _badgeModel.Revision.Value;

            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.Greater(_badgeModel.Revision.Value, revisionBefore);
        }

        [Test]
        public void Unlock_PublishesOneBadgeUnlockedMessagePerBadge()
        {
            BuildSystems();

            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.AreEqual(1, _unlockedBroker.Published.Count);
            Assert.AreEqual(FIRST_STEPS, _unlockedBroker.Published[0].BadgeId);

            // The same counter moving again re-fires nothing: the latch is one-way.
            _statsModel.TotalPiecesPlaced.Value = 51;
            Assert.AreEqual(1, _unlockedBroker.Published.Count);
        }

        [Test]
        public void Unlock_RecordsTheBadgeInThisRunsBuffer()
        {
            BuildSystems();

            _statsModel.TotalPiecesPlaced.Value = 50;
            _statsModel.TotalLinesCleared.Value = 100;

            Assert.AreEqual(2, _badgeModel.UnlockedThisRun.Count);
            Assert.AreEqual(FIRST_STEPS, _badgeModel.UnlockedThisRun[0]);
            Assert.AreEqual(LINE_CUTTER, _badgeModel.UnlockedThisRun[1]);
        }

        [Test]
        public void RunStarted_ClearsThisRunsBuffer()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;

            _runStartedBroker.Publish(new RunStartedMessage());

            Assert.AreEqual(0, _badgeModel.UnlockedThisRun.Count);
            Assert.IsTrue(_badgeSystem.IsClaimable(FIRST_STEPS), "Clearing the run buffer must not touch the claim state.");
        }

        [Test]
        public void ARestoredBadge_IsNeverInThisRunsBufferAndPublishesNothing()
        {
            PlayerPrefs.SetString(
                BADGE_SAVE_KEY,
                "{\"schemaVersion\":2,\"unlockedBadgeIds\":[\"" + FIRST_STEPS + "\"],\"claimedBadgeIds\":[]}");

            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.AreEqual(0, _badgeModel.UnlockedThisRun.Count);
            Assert.AreEqual(0, _unlockedBroker.Published.Count);
            Assert.IsTrue(_badgeSystem.IsClaimable(FIRST_STEPS));
        }

        [Test]
        public void ClaimReward_OnAClaimableBadge_CreditsExactlyTheConfiguredCoinsAndMarksClaimed()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;

            bool claimed = _badgeSystem.ClaimReward(FIRST_STEPS);

            Assert.IsTrue(claimed);
            Assert.AreEqual(FIRST_STEPS_REWARD, _profileModel.CoinBalance.Value);
            Assert.AreEqual(FIRST_STEPS_REWARD, PlayerPrefs.GetInt(COIN_BALANCE_KEY, 0));
            Assert.IsTrue(_badgeModel.IsClaimed(FIRST_STEPS));
            Assert.IsFalse(_badgeSystem.IsClaimable(FIRST_STEPS));
        }

        [Test]
        public void ClaimReward_Twice_PaysOnlyOnce()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.IsTrue(_badgeSystem.ClaimReward(FIRST_STEPS));
            Assert.IsFalse(_badgeSystem.ClaimReward(FIRST_STEPS));

            Assert.AreEqual(FIRST_STEPS_REWARD, _profileModel.CoinBalance.Value);
        }

        [Test]
        public void ClaimReward_OnALockedBadge_ChangesNothing()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 49;
            string saveBefore = PlayerPrefs.GetString(BADGE_SAVE_KEY, string.Empty);

            Assert.IsFalse(_badgeSystem.ClaimReward(FIRST_STEPS));

            Assert.AreEqual(0, _profileModel.CoinBalance.Value);
            Assert.AreEqual(saveBefore, PlayerPrefs.GetString(BADGE_SAVE_KEY, string.Empty));
        }

        [Test]
        public void ClaimReward_OnAZeroRewardBadge_ChangesNothing()
        {
            BuildSystems();
            _statsModel.TotalBoardWipes.Value = 5;
            string saveBefore = PlayerPrefs.GetString(BADGE_SAVE_KEY, string.Empty);

            Assert.IsTrue(_badgeModel.Badges[2].IsUnlocked);
            Assert.IsFalse(_badgeSystem.IsClaimable(TROPHY_ONLY));
            Assert.IsFalse(_badgeSystem.ClaimReward(TROPHY_ONLY));

            Assert.AreEqual(0, _profileModel.CoinBalance.Value);
            Assert.AreEqual(saveBefore, PlayerPrefs.GetString(BADGE_SAVE_KEY, string.Empty));
        }

        [Test]
        public void ClaimReward_OnAnUnknownBadge_ChangesNothing()
        {
            BuildSystems();

            Assert.IsFalse(_badgeSystem.ClaimReward("no_such_badge"));
            Assert.IsFalse(_badgeSystem.ClaimReward(null));
            Assert.AreEqual(0, _profileModel.CoinBalance.Value);
        }

        [Test]
        public void ClaimReward_BumpsTheModelRevision()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;
            int revisionBefore = _badgeModel.Revision.Value;

            _badgeSystem.ClaimReward(FIRST_STEPS);

            Assert.Greater(_badgeModel.Revision.Value, revisionBefore);
        }

        [Test]
        public void Claim_ThenANewSystem_StaysClaimedAndKeepsTheCoins()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;
            _badgeSystem.ClaimReward(FIRST_STEPS);
            DisposeSystems();

            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.IsTrue(_badgeModel.Badges[0].IsUnlocked);
            Assert.IsTrue(_badgeModel.IsClaimed(FIRST_STEPS));
            Assert.IsFalse(_badgeSystem.IsClaimable(FIRST_STEPS));
            Assert.AreEqual(FIRST_STEPS_REWARD, _profileModel.CoinBalance.Value);
        }

        [Test]
        public void Unlock_ThenANewSystemWithoutClaiming_IsStillClaimable()
        {
            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;
            DisposeSystems();

            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;

            Assert.IsTrue(_badgeSystem.IsClaimable(FIRST_STEPS));
            Assert.AreEqual(0, _profileModel.CoinBalance.Value);
        }

        [Test]
        public void Load_AVersionOneSave_TreatsEveryUnlockedBadgeAsAlreadyClaimed()
        {
            PlayerPrefs.SetString(
                BADGE_SAVE_KEY,
                "{\"schemaVersion\":1,\"unlockedBadgeIds\":[\"" + FIRST_STEPS + "\",\"" + LINE_CUTTER + "\"]}");

            BuildSystems();
            _statsModel.TotalPiecesPlaced.Value = 50;
            _statsModel.TotalLinesCleared.Value = 100;

            Assert.IsTrue(_badgeModel.Badges[0].IsUnlocked);
            Assert.IsTrue(_badgeModel.Badges[1].IsUnlocked);
            Assert.IsFalse(_badgeSystem.IsClaimable(FIRST_STEPS));
            Assert.IsFalse(_badgeSystem.IsClaimable(LINE_CUTTER));
            Assert.IsFalse(_badgeSystem.ClaimReward(FIRST_STEPS));
            Assert.AreEqual(0, _profileModel.CoinBalance.Value);

            // Written back as version 2 straight away, so the migration cannot run a second time.
            StringAssert.Contains("\"schemaVersion\":2", PlayerPrefs.GetString(BADGE_SAVE_KEY));
            StringAssert.Contains("claimedBadgeIds", PlayerPrefs.GetString(BADGE_SAVE_KEY));
        }

        [Test]
        public void Load_AVersionOneSave_OnlySeedsClaimsForBadgesItListed()
        {
            PlayerPrefs.SetString(
                BADGE_SAVE_KEY,
                "{\"schemaVersion\":1,\"unlockedBadgeIds\":[\"" + FIRST_STEPS + "\"]}");

            BuildSystems();
            _statsModel.TotalLinesCleared.Value = 100;

            Assert.IsFalse(_badgeSystem.IsClaimable(FIRST_STEPS));
            Assert.IsTrue(_badgeSystem.IsClaimable(LINE_CUTTER));
        }

        [Test]
        public void CoinRewardOf_ReadsTheCatalogAmount()
        {
            BuildSystems();

            Assert.AreEqual(FIRST_STEPS_REWARD, _badgeSystem.CoinRewardOf(FIRST_STEPS));
            Assert.AreEqual(LINE_CUTTER_REWARD, _badgeSystem.CoinRewardOf(LINE_CUTTER));
            Assert.AreEqual(0, _badgeSystem.CoinRewardOf(TROPHY_ONLY));
            Assert.AreEqual(0, _badgeSystem.CoinRewardOf("no_such_badge"));
        }

        [Test]
        public void BadgeConfig_WithANegativeCoinReward_IsInvalid()
        {
            var catalog = ScriptableObject.CreateInstance<BadgeCatalog>();
            JsonUtility.FromJsonOverwrite(
                "{\"_badges\":[{\"_id\":\"bad\",\"_statType\":0,\"_threshold\":1,\"_coinReward\":-5}]}", catalog);

            Assert.IsFalse(catalog.Badges[0].IsValid(out string error));
            StringAssert.Contains("Coin Reward", error);

            UnityEngine.Object.DestroyImmediate(catalog);
        }

        private void BuildSystems()
        {
            _badgeModel = new BadgeModel();
            _statsModel = new BadgeStatsModel();
            _profileModel = new ProfileModel();
            _unlockedBroker = new TestMessageBroker<BadgeUnlockedMessage>();
            _runStartedBroker = new TestMessageBroker<RunStartedMessage>();
            _currencySystem = CreateCurrencySystem(_profileModel);
            _badgeSystem = new BadgeSystem(
                _badgeModel, _statsModel, _catalog, _currencySystem, _unlockedBroker, _runStartedBroker);
        }

        private void DisposeSystems()
        {
            _badgeSystem.Dispose();
            _currencySystem.Dispose();
            _powerUpSystem.Dispose();
            _badgeSystem = null;
            _currencySystem = null;
            _powerUpSystem = null;
        }

        private CurrencySystem CreateCurrencySystem(ProfileModel profileModel)
        {
            var levelProgressionModel = new LevelProgressionModel();
            return new CurrencySystem(
                profileModel,
                new DailyAdGrantModel(),
                new CoinBundlePriceModel(),
                new ScoreModel(),
                levelProgressionModel,
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                ScriptableObject.CreateInstance<PowerUpPriceConfig>(),
                ScriptableObject.CreateInstance<PromotionConfig>(),
                CreatePowerUpSystem(levelProgressionModel),
                new StubCoinRewardSource(),
                new StubCoinPurchaseService(CoinPurchaseOutcome.Failed),
                StubPurchaseReceiptValidator.Rejecting(),
                new TestMessageBroker<ScoreConvertedToCoinsMessage>(),
                new TestMessageBroker<CoinsGrantedFromAdMessage>(),
                new TestMessageBroker<CoinsGrantedFromPurchaseMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                new TestMessageBroker<CoinProductsFetchedMessage>());
        }

        private PowerUpSystem CreatePowerUpSystem(LevelProgressionModel levelProgressionModel)
        {
            var boardModel = new BoardModel();
            var trayModel = new TrayModel();
            BoardSystem boardSystem = CreateBoardSystem(boardModel, trayModel);
            var ghostFitModel = new GhostFitModel();
            var appliedBroker = new TestMessageBroker<PowerUpAppliedMessage>();

            _powerUpSystem = new PowerUpSystem(
                new PowerUpModel(),
                levelProgressionModel,
                ScriptableObject.CreateInstance<LevelCatalog>(),
                new GameModeModel(),
                new PathRunModel(),
                boardModel,
                trayModel,
                boardSystem,
                CreateTimerRunSystem(boardSystem),
                new DoubleMultiplierSystem(
                    new DoubleMultiplierModel(),
                    new RunPauseModel(),
                    new TestMessageBroker<RunStartedMessage>(),
                    new TestMessageBroker<GameOverMessage>()),
                new GhostFitSystem(
                    ghostFitModel,
                    boardModel,
                    trayModel,
                    new ScoreModel(),
                    new TestMessageBroker<RunStartedMessage>(),
                    new TestMessageBroker<GameOverMessage>(),
                    new TestMessageBroker<PiecePlacedMessage>(),
                    appliedBroker),
                ghostFitModel,
                new StubRewardSource(),
                appliedBroker,
                new TestMessageBroker<PowerUpGrantedMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());

            return _powerUpSystem;
        }

        private static BoardSystem CreateBoardSystem(BoardModel boardModel, TrayModel trayModel)
        {
            return new BoardSystem(
                boardModel,
                trayModel,
                new ScoreGemProgressModel(),
                new VortexProgressModel(),
                new WeightedPieceDraw(),
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<PiecePlacedMessage>(),
                new TestMessageBroker<LinesClearedMessage>(),
                new TestMessageBroker<GameOverMessage>(),
                new TestMessageBroker<TrayRefilledMessage>(),
                new TestMessageBroker<ExplosiveCoreDetonatedMessage>(),
                new TestMessageBroker<LaserFiredMessage>(),
                new TestMessageBroker<PiercingRocketFiredMessage>(),
                new TestMessageBroker<VortexIslandFilledMessage>(),
                new TestMessageBroker<ChainLightningTriggeredMessage>(),
                new TestMessageBroker<CoinCellsClearedMessage>(),
                ScriptableObject.CreateInstance<CurrencyConfig>(),
                reinforcedCellSeeder: null);
        }

        private static TimerRunSystem CreateTimerRunSystem(BoardSystem boardSystem)
        {
            return new TimerRunSystem(
                new TimerModel(),
                new RunPauseModel(),
                new GameModeSystem(new GameModeModel(), boardSystem),
                new TimedModeSystem(new TimedModeModel(), ScriptableObject.CreateInstance<TimedModeConfig>()),
                boardSystem,
                new TestMessageBroker<RunStartedMessage>(),
                new TestMessageBroker<GameOverMessage>());
        }

        private static void DeleteKeys()
        {
            PlayerPrefs.DeleteKey(BADGE_SAVE_KEY);
            PlayerPrefs.DeleteKey(COIN_BALANCE_KEY);
            PlayerPrefs.DeleteKey(TOTAL_SCORE_EARNED_KEY);
            PlayerPrefs.DeleteKey(SCORE_CONVERTED_KEY);
            PlayerPrefs.DeleteKey(CONSUMED_TRANSACTION_IDS_KEY);

            foreach (PowerUpKind kind in Enum.GetValues(typeof(PowerUpKind)))
            {
                PlayerPrefs.DeleteKey(PowerUpInventoryKey.For(kind));
            }
        }

        /// <summary>Never reached by these tests; the coin-ad seam has to be satisfied to build the
        /// currency System at all.</summary>
        private sealed class StubCoinRewardSource : ICoinRewardSource
        {
            public UniTask<CoinRewardResult> RequestCoinRewardAsync(int amount, CancellationToken cancellationToken)
                => UniTask.FromResult(new CoinRewardResult(amount, false));
        }

        private sealed class StubRewardSource : IRewardSource
        {
            public UniTask<RewardResult> RequestRewardAsync(PowerUpKind kind, CancellationToken cancellationToken)
                => UniTask.FromResult(new RewardResult(kind, false));
        }
    }
}
