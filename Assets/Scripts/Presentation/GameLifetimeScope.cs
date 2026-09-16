using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Localization;
using MustyBlockBlast.Presentation.Services;
using MustyBlockBlast.Presentation.Views;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MustyBlockBlast.Presentation
{
    /// <summary>Single composition root for the gameplay scene. The only place bindings happen.</summary>
    public sealed class GameLifetimeScope : LifetimeScope
    {
        [Header("Settings")]
        [Tooltip("Selectable themes in display order. The first entry is the default (Yaz).")]
        [SerializeField] private ThemeDefinition[] _availableThemes;

        [Tooltip("Selectable round lengths for timed mode. Required — timed runs cannot be configured without it.")]
        [SerializeField] private TimedModeConfig _timedModeConfig;

        [Tooltip("Authored level content. Required — without it there is no objective to show or clear.")]
        [SerializeField] private LevelCatalog _levelCatalog;

        [Tooltip("Authored badge content. Required — without it there are no badges to track or unlock.")]
        [SerializeField] private BadgeCatalog _badgeCatalog;

        [Tooltip("Score-to-coin rate and the rewarded-ad coin grant. Required — without it there is no economy.")]
        [SerializeField] private CurrencyConfig _currencyConfig;

        [Tooltip("Coin price of each power-up kind. Required — without it the shop has nothing to charge.")]
        [SerializeField] private PowerUpPriceConfig _powerUpPriceConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterMessaging(builder);
            RegisterModels(builder);
            RegisterSystems(builder);
            RegisterViews(builder);

            // ScoreSystem and TimedHighScoreSystem subscribe in their constructors, so they must exist
            // before the first run starts, and LeaderboardSystem likewise subscribes in its constructor
            // and must be listening before the first game over rather than being constructed by one.
            // SettingsSystem, SfxSystem and LocalizationSystem load their
            // persisted settings in their constructors, so they must exist before any View subscribes
            // to SettingsModel/SfxModel/LocalizationModel in Start(). PowerUpSystem loads the persisted
            // inventory in its constructor and PowerUpScoreSystem subscribes in its own, so neither may
            // wait for a first lazy resolve.
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<ScoreSystem>();
                container.Resolve<TimedHighScoreSystem>();
                container.Resolve<LeaderboardSystem>();
                container.Resolve<SettingsSystem>();
                container.Resolve<SfxSystem>();
                container.Resolve<LocalizationSystem>();
                // Subscribes in its constructor, so it must be listening before the first placement
                // rather than waiting for a lazy resolve. PowerUpSystem depends on it and would
                // construct it anyway; resolving it here says so rather than relying on that.
                container.Resolve<GhostFitSystem>();
                container.Resolve<PowerUpSystem>();
                container.Resolve<PowerUpScoreSystem>();

                // Loads the persisted coin balance and score-conversion counters in its constructor,
                // like PowerUpSystem, and subscribes to GameOverMessage there too — it must be listening
                // before the first run ends, or that run's score would never reach the convertible pool
                // and would be lost to the player for good.
                //
                // Must stay below PowerUpSystem: a coin purchase debits here and grants there, so it
                // takes that system as a constructor dependency. The container would build it anyway;
                // this order says so rather than relying on it.
                container.Resolve<CurrencySystem>();

                // Subscribes in its constructor, like PowerUpScoreSystem: it must be listening before
                // the first placement can detonate a core, not be constructed by one.
                container.Resolve<ExplosiveCoreScoreSystem>();
                container.Resolve<LaserScoreSystem>();
                container.Resolve<PiercingRocketScoreSystem>();

                // Subscribes to ScoreModel.Streak in its constructor, so it must be watching before the
                // first placement can build a streak — nothing else resolves it, so without this line
                // it would never be constructed at all.
                container.Resolve<LaserSpawnSystem>();

                // Same reason, same seam: it watches the streak for the golden piece's trigger and
                // nothing else would ever construct it.
                container.Resolve<GoldenPieceTriggerSystem>();

                // Subscribes in its constructor, like the systems above.
                //
                // DO NOT MOVE THIS ABOVE ScoreSystem. Both subscribe to PiecePlacedMessage, and
                // MessagePipe invokes handlers in subscription order — which, because both subscribe in
                // their constructors, is exactly the resolve order written here. ObjectiveSystem
                // publishes ObjectiveCompletedMessage from inside its own PiecePlacedMessage handler,
                // and GameMode.Path ends the run on that message with ScoreModel.Score as the run's
                // final figure (LevelProgressionSystem.CompletePathLevel). If ObjectiveSystem ran
                // first, that figure would be missing the very placement that completed the level, and
                // both the end-of-run card and the path total would be quietly short by it. Nothing
                // enforces this at compile time, but it is covered at runtime by
                // LevelProgressionSystemPathModeTests.
                // ARealPlacementThatCompletesTheLevel_BanksTheFullScoreIncludingThePlacementsOwnPoints,
                // which constructs the two systems in this same order and fails if it is reversed.
                container.Resolve<ObjectiveSystem>();

                // Must come after ObjectiveSystem: it loads the saved level and writes the current
                // objective into ObjectiveModel, and ObjectiveSystem must already be subscribed to
                // placements by the time that objective can be progressed.
                container.Resolve<LevelProgressionSystem>();

                // Subscribes in its constructor and loads the lifetime counters there too, so it must
                // exist before the first placement — and before BadgeSystem, which reads those
                // already-loaded counters at its own construction to decide what is already unlocked.
                container.Resolve<BadgeStatsSystem>();
                container.Resolve<BadgeSystem>();
            });
        }

        private static void RegisterMessaging(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<RunStartedMessage>(options);
            builder.RegisterMessageBroker<PiecePlacedMessage>(options);
            builder.RegisterMessageBroker<LinesClearedMessage>(options);
            builder.RegisterMessageBroker<ScoreChangedMessage>(options);
            builder.RegisterMessageBroker<NewRecordMessage>(options);
            builder.RegisterMessageBroker<BonusScoredMessage>(options);
            builder.RegisterMessageBroker<GameOverMessage>(options);
            builder.RegisterMessageBroker<PlaySfxRequestedMessage>(options);
            builder.RegisterMessageBroker<PlayMusicRequestedMessage>(options);
            builder.RegisterMessageBroker<StopMusicRequestedMessage>(options);
            builder.RegisterMessageBroker<TrayRefilledMessage>(options);
            builder.RegisterMessageBroker<PowerUpAppliedMessage>(options);
            builder.RegisterMessageBroker<PowerUpGrantedMessage>(options);
            builder.RegisterMessageBroker<ExplosiveCoreDetonatedMessage>(options);
            builder.RegisterMessageBroker<LaserFiredMessage>(options);
            builder.RegisterMessageBroker<PiercingRocketFiredMessage>(options);
            builder.RegisterMessageBroker<VortexPulledMessage>(options);
            builder.RegisterMessageBroker<ChainLightningTriggeredMessage>(options);
            builder.RegisterMessageBroker<ObjectiveProgressChangedMessage>(options);
            builder.RegisterMessageBroker<ObjectiveCompletedMessage>(options);
            builder.RegisterMessageBroker<LevelAdvancedMessage>(options);
            builder.RegisterMessageBroker<ScoreConvertedToCoinsMessage>(options);
            builder.RegisterMessageBroker<CoinsGrantedFromAdMessage>(options);
        }

        // Instance method: the theme list and the timed-mode config are scene-configured on this
        // MonoBehaviour.
        private void RegisterModels(IContainerBuilder builder)
        {
            builder.RegisterInstance<IReadOnlyList<ThemeDefinition>>(
                _availableThemes ?? new ThemeDefinition[0]);
            builder.RegisterInstance(ResolveTimedModeConfig());
            builder.RegisterInstance(ResolveLevelCatalog());
            builder.RegisterInstance(ResolveBadgeCatalog());
            builder.RegisterInstance(ResolveCurrencyConfig());
            builder.RegisterInstance(ResolvePowerUpPriceConfig());

            // Languages come from the project's Locale assets rather than a scene field: a new
            // language is a Locale asset plus a String Table column, with no scene edit.
            builder.RegisterInstance<IReadOnlyList<LocaleDefinition>>(UnityLocaleCatalog.Build());

            builder.Register<BoardModel>(Lifetime.Singleton);
            builder.Register<TrayModel>(Lifetime.Singleton);
            builder.Register<PerfectRoundModel>(Lifetime.Singleton);
            builder.Register<ScoreModel>(Lifetime.Singleton);
            builder.Register<TimedHighScoreModel>(Lifetime.Singleton);
            builder.Register<GameModeModel>(Lifetime.Singleton);
            builder.Register<TimedModeModel>(Lifetime.Singleton);
            builder.Register<TimerModel>(Lifetime.Singleton);
            builder.Register<RunPauseModel>(Lifetime.Singleton);
            builder.Register<SfxModel>(Lifetime.Singleton);
            builder.Register<SettingsModel>(Lifetime.Singleton);
            builder.Register<LocalizationModel>(Lifetime.Singleton);
            builder.Register<PowerUpModel>(Lifetime.Singleton);
            builder.Register<DoubleMultiplierModel>(Lifetime.Singleton);
            builder.Register<GhostFitModel>(Lifetime.Singleton);
            builder.Register<ObjectiveModel>(Lifetime.Singleton);
            builder.Register<LevelProgressionModel>(Lifetime.Singleton);
            builder.Register<PathRunModel>(Lifetime.Singleton);
            builder.Register<BadgeStatsModel>(Lifetime.Singleton);
            builder.Register<BadgeModel>(Lifetime.Singleton);
            builder.Register<PendingScoreModel>(Lifetime.Singleton);
            builder.Register<ProfileModel>(Lifetime.Singleton);
            builder.Register<LeaderboardModel>(Lifetime.Singleton);
        }

        /// <summary>
        /// Same defensive shape as <see cref="ResolveTimedModeConfig"/>: a default-valued instance boots
        /// the scene on the built-in placeholder rate and one readable error, which beats an opaque
        /// container failure deep inside a null instance registration.
        /// </summary>
        private CurrencyConfig ResolveCurrencyConfig()
        {
            if (_currencyConfig != null)
            {
                return _currencyConfig;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(CurrencyConfig)} assigned. " +
                "Score conversion is falling back to the built-in default rate.", this);
            return ScriptableObject.CreateInstance<CurrencyConfig>();
        }

        /// <summary>
        /// Same defensive shape as <see cref="ResolveCurrencyConfig"/>: a default-valued instance boots
        /// the scene on the built-in placeholder prices — which price every kind, so the shop still
        /// works — and one readable error, which beats an opaque container failure deep inside a null
        /// instance registration.
        /// </summary>
        private PowerUpPriceConfig ResolvePowerUpPriceConfig()
        {
            if (_powerUpPriceConfig != null)
            {
                return _powerUpPriceConfig;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(PowerUpPriceConfig)} assigned. " +
                "The power-up shop is falling back to the built-in default prices.", this);
            return ScriptableObject.CreateInstance<PowerUpPriceConfig>();
        }

        /// <summary>
        /// Same defensive shape as <see cref="ResolveLevelCatalog"/>: an empty catalog boots the scene
        /// with an empty badge wall and one readable error, which beats an opaque container failure
        /// deep inside a null instance registration.
        /// </summary>
        private BadgeCatalog ResolveBadgeCatalog()
        {
            if (_badgeCatalog != null)
            {
                return _badgeCatalog;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(BadgeCatalog)} assigned. " +
                "No badges will be tracked or unlocked.", this);
            return ScriptableObject.CreateInstance<BadgeCatalog>();
        }

        /// <summary>
        /// Same defensive shape as <see cref="ResolveTimedModeConfig"/>: an empty catalog boots the
        /// scene with no objective shown and one readable error, which beats an opaque container
        /// failure deep inside a null instance registration.
        /// </summary>
        private LevelCatalog ResolveLevelCatalog()
        {
            if (_levelCatalog != null)
            {
                return _levelCatalog;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(LevelCatalog)} assigned. " +
                "No level objectives will be available.", this);
            return ScriptableObject.CreateInstance<LevelCatalog>();
        }

        /// <summary>
        /// The config is a required scene reference, but registering a null instance fails deep
        /// inside the container with an opaque error. Falling back to a default-valued instance keeps
        /// the scene bootable and turns the mistake into one readable console line.
        /// </summary>
        private TimedModeConfig ResolveTimedModeConfig()
        {
            if (_timedModeConfig != null)
            {
                return _timedModeConfig;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(TimedModeConfig)} assigned. " +
                "Timed mode is falling back to the built-in default durations.", this);
            return ScriptableObject.CreateInstance<TimedModeConfig>();
        }

        private static void RegisterSystems(IContainerBuilder builder)
        {
            builder.Register<WeightedPieceDraw>(Lifetime.Singleton);
            // Each .As<IScoreRule>() adds to the same collection binding, so ScoreSystem's
            // IEnumerable<IScoreRule> resolves all of them. A new bonus = one more line here.
            builder.Register<PlacementScoreRule>(Lifetime.Singleton).As<IScoreRule>();
            builder.Register<LineClearScoreRule>(Lifetime.Singleton).As<IScoreRule>();
            builder.Register<MonochromeScoreRule>(Lifetime.Singleton).As<IScoreRule>();
            builder.Register<MultiClearStreakScoreRule>(Lifetime.Singleton).As<IScoreRule>();
            builder.Register<CumulativeMultiClearMilestoneScoreRule>(Lifetime.Singleton).As<IScoreRule>();
            builder.Register<BoardWipeScoreRule>(Lifetime.Singleton).As<IScoreRule>();
            builder.Register<ScoreSystem>(Lifetime.Singleton);
            builder.Register<TimedHighScoreSystem>(Lifetime.Singleton);
            builder.Register<LeaderboardSystem>(Lifetime.Singleton);

            // Not resolved eagerly in the build callback on purpose: it is a constructor dependency of
            // LeaderboardSystem, which is, so the container builds it — and with it restores and starts
            // draining the previous session's backlog — before the first game over regardless.
            builder.Register<PendingScoreQueueSystem>(Lifetime.Singleton);
            builder.Register<SfxSystem>(Lifetime.Singleton).As<ISfxService>().AsSelf();
            builder.Register<MusicSystem>(Lifetime.Singleton).As<IMusicService>().AsSelf();
            builder.Register<SettingsSystem>(Lifetime.Singleton);
            builder.Register<UnityLocalizedStringSource>(Lifetime.Singleton).As<ILocalizedStringSource>();
            builder.Register<LocalizationSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BoardSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<GameModeSystem>(Lifetime.Singleton);
            builder.Register<TimedModeSystem>(Lifetime.Singleton);

            // Registered here as well as in SplashLifetimeScope, for the same reason the language and
            // audio stacks are: the two scenes are loadable independently and share no container, and
            // this one is the scene a developer presses Play on. Signing in twice costs nothing — the
            // UGS session is process-wide, so whichever scene gets there second short-circuits.
            builder.Register<UnityAuthService>(Lifetime.Singleton).As<IAuthService>();
            builder.RegisterEntryPoint<AuthBootSystem>(Lifetime.Singleton);

            // Submission needs the identity above, so it is bound next to it. LeaderboardSystem is
            // registered with the other score systems — this is only the backend it talks through.
            builder.Register<UnityLeaderboardsService>(Lifetime.Singleton).As<ILeaderboardsService>();

            // The read half of the same backend. Not resolved eagerly: it subscribes to nothing and
            // starts no work of its own, so the first open of the leaderboard card is exactly when it
            // needs to exist. AsSelf because LeaderboardPanelView asks for the concrete system — there
            // is no second implementation to hide behind an interface.
            builder.Register<LeaderboardQuerySystem>(Lifetime.Singleton).AsSelf();

            // Owns the player's public identity and is the only writer of it, so it is bound next to the
            // auth service it publishes the name through. AsSelf because ProfilePanelView asks for the
            // concrete system — there is no second implementation to hide behind an interface.
            builder.Register<ProfileSystem>(Lifetime.Singleton).AsSelf();

            // Credential acquisition, one per store. Both are Presentation-side because both wrap a
            // native plugin, and both compile to a throwing stub off their own platform — see their
            // scripting defines.
            builder.Register<AppleSignInProvider>(Lifetime.Singleton);
            builder.Register<GooglePlayGamesSignInProvider>(Lifetime.Singleton);

            // Decides whether a finished run's score is submitted now or banked for later, so it is
            // bound next to the backend it guards.
            builder.Register<UnityConnectivityService>(Lifetime.Singleton).As<IConnectivityService>();

            // Always-granting stub until a rewarded-ad SDK is wired up; swapping it is one line here.
            builder.Register<DeterministicRewardSource>(Lifetime.Singleton).As<IRewardSource>().AsSelf();

            // The coin half of the same stub, on its own seam: an ad that pays coins and an ad that pays
            // a power-up are different offers with different outcomes, so they get different interfaces.
            builder.Register<DeterministicCoinRewardSource>(Lifetime.Singleton)
                .As<ICoinRewardSource>().AsSelf();

            // Before PowerUpSystem only for readability — PowerUpSystem takes it as a constructor
            // dependency, so the container orders the two itself.
            builder.Register<GhostFitSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PowerUpSystem>(Lifetime.Singleton).AsSelf();

            // Owns the currency slice of ProfileModel and is the only writer of it, so it is bound next
            // to the coin reward source it grants through. AsSelf because CoinConversionView and
            // PowerUpShopView both ask for the concrete system — there is no second implementation to
            // hide behind an interface.
            //
            // After PowerUpSystem for the same readability reason: it takes that system as a
            // constructor dependency (a coin purchase is a debit here and a grant there), so the
            // container orders the two itself either way.
            builder.Register<CurrencySystem>(Lifetime.Singleton).AsSelf();

            builder.Register<PowerUpScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<ExplosiveCoreScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<LaserScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PiercingRocketScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<LaserSpawnSystem>(Lifetime.Singleton).AsSelf();

            // Watches the combo streak and asks BoardSystem to inject a golden 1x1 at the next refill.
            builder.Register<GoldenPieceTriggerSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<ObjectiveSystem>(Lifetime.Singleton);
            builder.Register<LevelProgressionSystem>(Lifetime.Singleton);
            builder.Register<BadgeStatsSystem>(Lifetime.Singleton);
            builder.Register<BadgeSystem>(Lifetime.Singleton);

            // Entry point because it is an ITickable: the countdown is driven by VContainer's player
            // loop, not by a MonoBehaviour Update.
            builder.RegisterEntryPoint<TimerRunSystem>(Lifetime.Singleton).AsSelf();

            // Same reason: the 2x window has to expire on its own schedule, whether or not the player
            // places anything while it is open.
            builder.RegisterEntryPoint<DoubleMultiplierSystem>(Lifetime.Singleton).AsSelf();
        }

        private static void RegisterViews(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardView>();
            builder.RegisterComponentInHierarchy<BackgroundView>();
            builder.RegisterComponentInHierarchy<SettingsButtonView>();
            builder.RegisterComponentInHierarchy<SettingsPanelView>();
            builder.RegisterComponentInHierarchy<LevelPathButtonView>();
            builder.RegisterComponentInHierarchy<LevelPathPanelView>();
            builder.RegisterComponentInHierarchy<BadgesButtonView>();
            builder.RegisterComponentInHierarchy<BadgesPanelView>();
            builder.RegisterComponentInHierarchy<ProfileButtonView>();
            builder.RegisterComponentInHierarchy<ProfilePanelView>();
            builder.RegisterComponentInHierarchy<LeaderboardButtonView>();
            builder.RegisterComponentInHierarchy<LeaderboardPanelView>();
            builder.RegisterComponentInHierarchy<PowerUpShopButtonView>();
            builder.RegisterComponentInHierarchy<PowerUpShopView>();
            builder.RegisterComponentInHierarchy<PieceTrayView>();
            builder.RegisterComponentInHierarchy<HoldSlotView>();
            builder.RegisterComponentInHierarchy<ScoreView>();
            builder.RegisterComponentInHierarchy<TimerHudView>();
            builder.RegisterComponentInHierarchy<DoubleMultiplierHudView>();
            builder.RegisterComponentInHierarchy<GhostFitView>();
            builder.RegisterComponentInHierarchy<LineClearBurstView>();
            builder.RegisterComponentInHierarchy<BonusFeedbackView>();
            builder.RegisterComponentInHierarchy<PowerUpInventoryView>();
            builder.RegisterComponentInHierarchy<ObjectiveIconContainerView>();
            builder.RegisterComponentInHierarchy<ObjectiveInfoPopupView>();
            builder.RegisterComponentInHierarchy<GameOverView>();
            builder.RegisterComponentInHierarchy<CoinConversionView>();
            builder.RegisterComponentInHierarchy<BoardInputView>();
            builder.RegisterComponentInHierarchy<SfxPlayerView>();
            builder.RegisterComponentInHierarchy<MusicPlayerView>();
            builder.RegisterComponentInHierarchy<GameMusicView>();
            builder.RegisterComponentInHierarchy<LineClearSfxView>();
            builder.RegisterComponentInHierarchy<BlockPlaceSfxView>();
            builder.RegisterComponentInHierarchy<GameOverSfxView>();
            builder.RegisterComponentInHierarchy<NewRecordSfxView>();
            builder.RegisterComponentInHierarchy<BonusSfxView>();
            builder.RegisterComponentInHierarchy<PiercingRocketSfxView>();
        }
    }
}
