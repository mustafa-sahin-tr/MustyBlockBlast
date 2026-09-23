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
using Mtafasahin.MobileServices;

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

        [Tooltip("Authored objective glyphs. Optional — a missing catalog or entry falls back to the procedural glyph.")]
        [SerializeField] private ObjectiveIconCatalog _objectiveIconCatalog;

        [Tooltip("Score-to-coin rate and the rewarded-ad coin grant. Required — without it there is no economy.")]
        [SerializeField] private CurrencyConfig _currencyConfig;

        [Tooltip("Coin price of each power-up kind. Required — without it the shop has nothing to charge.")]
        [SerializeField] private PowerUpPriceConfig _powerUpPriceConfig;

        [Tooltip("The power-up shop's own colours — the one card that does not follow the theme. Optional: "
            + "the built-in defaults are the finished design.")]
        [SerializeField] private ShopPaletteConfig _shopPaletteConfig;

        [Tooltip("Coin bundles buyable with real money, and what each pays. Required — without it the "
            + "storefront has nothing to sell and no purchase can be priced.")]
        [SerializeField] private CoinBundleConfig _coinBundleConfig;

        [Tooltip("Store product id of the one-time Remove Ads purchase. Required — without it there is "
            + "no product to register with the store and nothing to sell.")]
        [SerializeField] private RemoveAdsProductConfig _removeAdsProductConfig;

        [Tooltip("Time-limited discounts on power-up prices. Optional — an unassigned or empty config "
            + "simply means no sale is running and every kind costs its standard price.")]
        [SerializeField] private PromotionConfig _promotionConfig;

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

                // Loads the persisted ad-removal flag in its constructor, like CurrencySystem loads the
                // balance, so it must have run before any View subscribes to ProfileModel.AdsRemoved in
                // Start() — otherwise an owning player's settings card would paint "not bought" and only
                // correct itself on the next write, which for a one-way flag never comes.
                container.Resolve<AdRemovalSystem>();

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

                // Same reason again: it watches the streak for the coin cell's escalating drops.
                container.Resolve<CoinStreakEscalationSystem>();

                // Subscribes to RunStartedMessage and PiecePlacedMessage in its constructor, so it must
                // be listening before the first run opens — nothing else resolves it either.
                container.Resolve<LevelCoinCellSeedSystem>();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Subscribes to RunStartedMessage and friends in its constructor, so it must be
                // listening before the first run opens — nothing else resolves it either.
                //
                // Resolved here, deliberately before ObjectiveSystem: that System publishes
                // GameOverMessage synchronously from inside its own PiecePlacedMessage handler when a
                // placement completes a Path level, and MessagePipe calls subscribers in subscription
                // order. Subscribing after ObjectiveSystem would make this system's own "GAME OVER"
                // line land in the log before the "PLACE" line for the very placement that caused it —
                // still correct gameplay, but a confusing read. Subscribing first means this system's
                // PLACE line is always written before any GameOverMessage a later subscriber raises
                // out of that same placement.
                container.Resolve<SessionRecorderSystem>();
#endif

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

                // Subscribes to SpecialCellSpawnedMessage, PowerUpGrantedMessage, HoldFirstUseMessage
                // and SpecialPieceSpawnedMessage in its constructor, so it must be listening before the
                // first of any of those can fire, not constructed by one. Also runs its one-shot
                // already-unlocked-power-ups migration here, on boot.
                container.Resolve<InfoPopupSystem>();
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

            // The no-moves rescue's "that ending was taken back" (issue #370): BoardSystem publishes it
            // on an accepted rescue; TimerRunSystem releases the clock on it. The end-of-run card and
            // the strip will listen too once #371 wires the offer's UI.
            builder.RegisterMessageBroker<RunRescuedMessage>(options);

            // Timed mode's "a line clear gave the clock seconds back" (issue #319): TimerRunSystem
            // publishes it after extending the countdown; TimerHudView flashes the "+Ns" on it.
            builder.RegisterMessageBroker<TimeExtendedMessage>(options);
            builder.RegisterMessageBroker<PlaySfxRequestedMessage>(options);
            builder.RegisterMessageBroker<PlayMusicRequestedMessage>(options);
            builder.RegisterMessageBroker<StopMusicRequestedMessage>(options);
            builder.RegisterMessageBroker<TrayRefilledMessage>(options);
            builder.RegisterMessageBroker<PowerUpAppliedMessage>(options);
            builder.RegisterMessageBroker<PowerUpGrantedMessage>(options);
            builder.RegisterMessageBroker<PowerUpGrantAnimationCompletedMessage>(options);
            builder.RegisterMessageBroker<BadgeUnlockedMessage>(options);
            builder.RegisterMessageBroker<ExplosiveCoreDetonatedMessage>(options);
            builder.RegisterMessageBroker<LaserFiredMessage>(options);
            builder.RegisterMessageBroker<PiercingRocketFiredMessage>(options);
            builder.RegisterMessageBroker<VortexIslandFilledMessage>(options);
            builder.RegisterMessageBroker<ChainLightningTriggeredMessage>(options);
            builder.RegisterMessageBroker<ObjectiveProgressChangedMessage>(options);
            builder.RegisterMessageBroker<ObjectiveCompletedMessage>(options);
            builder.RegisterMessageBroker<LevelAdvancedMessage>(options);
            builder.RegisterMessageBroker<ScoreConvertedToCoinsMessage>(options);
            builder.RegisterMessageBroker<CoinsGrantedFromAdMessage>(options);
            builder.RegisterMessageBroker<CoinCellsClearedMessage>(options);
            builder.RegisterMessageBroker<CoinsGrantedFromPurchaseMessage>(options);
            builder.RegisterMessageBroker<CoinProductsFetchedMessage>(options);

            // Info popup infrastructure: InfoPopupSystem subscribes to SpecialCellSpawnedMessage,
            // SpecialPieceSpawnedMessage and HoldFirstUseMessage (PowerUpGrantedMessage, registered
            // elsewhere, is its fourth trigger). PowerUpUnlockedMessage was the old TutorialSystem's
            // trigger and has no subscriber left today; kept registered since PowerUpSystem still
            // publishes it.
            builder.RegisterMessageBroker<PowerUpUnlockedMessage>(options);
            builder.RegisterMessageBroker<SpecialCellSpawnedMessage>(options);
            builder.RegisterMessageBroker<SpecialPieceSpawnedMessage>(options);
            builder.RegisterMessageBroker<HoldFirstUseMessage>(options);
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
            builder.RegisterInstance(ResolveObjectiveIconCatalog());
            builder.RegisterInstance(ResolveCurrencyConfig());
            builder.RegisterInstance(ResolvePowerUpPriceConfig());
            builder.RegisterInstance(ResolveShopPaletteConfig());
            builder.RegisterInstance(ResolveCoinBundleConfig());
            builder.RegisterInstance(ResolveRemoveAdsProductConfig());
            builder.RegisterInstance(ResolvePromotionConfig());

            // Languages come from the project's Locale assets rather than a scene field: a new
            // language is a Locale asset plus a String Table column, with no scene edit.
            builder.RegisterInstance<IReadOnlyList<LocaleDefinition>>(UnityLocaleCatalog.Build());

            builder.Register<BoardModel>(Lifetime.Singleton);
            builder.Register<TrayModel>(Lifetime.Singleton);
            builder.Register<ScoreGemProgressModel>(Lifetime.Singleton);
            builder.Register<VortexProgressModel>(Lifetime.Singleton);
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

            // Device-local, deliberately not carried by ProfileModel — see DailyAdGrantModel (issue #257).
            builder.Register<DailyAdGrantModel>(Lifetime.Singleton);

            // Session-local store answer, not persisted — see CoinBundlePriceModel (issue #256).
            builder.Register<CoinBundlePriceModel>(Lifetime.Singleton);
            builder.Register<LeaderboardModel>(Lifetime.Singleton);
            builder.Register<InfoPopupModel>(Lifetime.Singleton);
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
        /// Same defensive shape as <see cref="ResolvePowerUpPriceConfig"/>: a default-valued instance
        /// boots the scene on the built-in placeholder line-up — which describes four sellable bundles,
        /// so the storefront still works — and one readable error, which beats an opaque container
        /// failure deep inside a null instance registration.
        /// </summary>
        private CoinBundleConfig ResolveCoinBundleConfig()
        {
            if (_coinBundleConfig != null)
            {
                return _coinBundleConfig;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(CoinBundleConfig)} assigned. " +
                "Coin bundle purchases are falling back to the built-in placeholder line-up.", this);
            return ScriptableObject.CreateInstance<CoinBundleConfig>();
        }

        /// <summary>
        /// The same shape as <see cref="ResolvePromotionConfig"/>, and quiet for the same reason: the
        /// palette's code defaults <em>are</em> the shipped design, so a scene without the asset draws the
        /// shop exactly as one with it — the asset only exists so the colours can be retuned without a
        /// code change.
        /// </summary>
        private ShopPaletteConfig ResolveShopPaletteConfig()
            => _shopPaletteConfig != null ? _shopPaletteConfig : ScriptableObject.CreateInstance<ShopPaletteConfig>();

        /// <summary>
        /// Same defensive shape as <see cref="ResolveCoinBundleConfig"/>: a default-valued instance
        /// boots the scene on the built-in placeholder SKU — which names a sellable product, so the
        /// settings card still offers it — and one readable error, which beats an opaque container
        /// failure deep inside a null instance registration.
        /// </summary>
        private RemoveAdsProductConfig ResolveRemoveAdsProductConfig()
        {
            if (_removeAdsProductConfig != null)
            {
                return _removeAdsProductConfig;
            }

            Debug.LogError(
                $"{nameof(GameLifetimeScope)} has no {nameof(RemoveAdsProductConfig)} assigned. " +
                "The Remove Ads purchase is falling back to the built-in placeholder SKU.", this);
            return ScriptableObject.CreateInstance<RemoveAdsProductConfig>();
        }

        /// <summary>
        /// The same defensive shape as <see cref="ResolveCoinBundleConfig"/> with one difference: no
        /// error is logged. Every other config here is load-bearing, so its absence is a misconfigured
        /// scene worth shouting about; a promotion config's absence is an ordinary state — no campaign
        /// is running. A default instance is still registered rather than a null, because the container
        /// must hand <see cref="CurrencySystem"/> something to ask; it carries
        /// <see cref="PromotionConfig"/>'s demonstration rows, which is why the field should be assigned
        /// — the asset, not the fallback, is where a real campaign is authored.
        /// </summary>
        private PromotionConfig ResolvePromotionConfig()
            => _promotionConfig != null ? _promotionConfig : ScriptableObject.CreateInstance<PromotionConfig>();

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
        /// Unlike the other catalogs this one is optional by design — every objective type still has a
        /// procedural glyph — so a missing asset only warns rather than errors.
        /// </summary>
        private ObjectiveIconCatalog ResolveObjectiveIconCatalog()
        {
            if (_objectiveIconCatalog != null)
            {
                return _objectiveIconCatalog;
            }

            Debug.LogWarning(
                $"{nameof(GameLifetimeScope)} has no {nameof(ObjectiveIconCatalog)} assigned. " +
                "Objective icons will use the procedural glyphs.", this);
            return ScriptableObject.CreateInstance<ObjectiveIconCatalog>();
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

            // After BoardSystem on purpose: VContainer runs IStartable entry points in registration
            // order, and the level path picker this opens (issue #379) belongs over an already-started
            // run. The request it consumes is written by the mode-select scene when Macera Modu is
            // picked; LevelPathOpenRequestSystem is that request's one reader and writer, registered
            // in both scopes for the same reason the language stack is.
            builder.Register<LevelPathOpenRequestSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<PendingLevelPathOpenSystem>(Lifetime.Singleton);
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

#if UNITY_EDITOR || !UNITY_ANDROID
            // Always-granting stubs for the Editor, EditMode tests and every platform AdMob is not yet
            // wired for (issue #380 is Android-only; iOS is tracked separately). Kept so in-editor
            // iteration and the test suite never depend on an ad SDK — the real binding is the #else.
            builder.Register<DeterministicRewardSource>(Lifetime.Singleton).As<IRewardSource>().AsSelf();

            // The coin half of the same stub, on its own seam: an ad that pays coins and an ad that pays
            // a power-up are different offers with different outcomes, so they get different interfaces.
            builder.Register<DeterministicCoinRewardSource>(Lifetime.Singleton)
                .As<ICoinRewardSource>().AsSelf();

            // The third seam of the same stub (issue #370): the ad that buys back a no-moves ending.
            // Its own interface for the reason the coin one has its own — a rescue is not an inventory
            // item — and its presence is what makes BoardSystem mark a NoMovesLeft ending rescuable.
            builder.Register<DeterministicRescueRewardSource>(Lifetime.Singleton)
                .As<IRescueRewardSource>().AsSelf();
#else
            // The real thing on Android devices (issue #380): Google AdMob behind all three seams. One
            // class because the three are one mechanic underneath — load a rewarded ad, show it, pay
            // only on the SDK's reward-earned callback — and the only type in the project that touches
            // the ad SDK. Ships with Google's public TEST ids; see the class for the swap-out note.
            builder.Register<AdMobRewardSource>(Lifetime.Singleton)
                .As<IRewardSource>().As<ICoinRewardSource>().As<IRescueRewardSource>().AsSelf();

            // Runs consent + SDK init at boot rather than on the player's first reward request, per
            // Google's own latency guidance. AsSelf above is what lets this take the concrete type
            // directly — there is nothing to warm up behind the Deterministic stubs in the #if branch,
            // so this entry point only exists where AdMobRewardSource itself does.
            builder.RegisterEntryPoint<AdWarmUpSystem>();
#endif

            // The real-money half, and the one binding here that is not a stub: this is the actual
            // Unity IAP integration, and the only type in the project that touches that SDK.
            builder.Register<UnityCoinPurchaseService>(Lifetime.Singleton).As<ICoinPurchaseService>();

            // DO NOT SHIP THIS BINDING. It approves every receipt on the device's word alone — see the
            // class doc. It is registered because no validation backend exists yet, on the same
            // explicit-placeholder footing as the two ad stubs above, and swapping it for a real
            // server-backed validator is this one line. Until that line changes, no real money should
            // be taken from a real player.
            builder.Register<DeterministicPurchaseReceiptValidator>(Lifetime.Singleton)
                .As<IPurchaseReceiptValidator>().AsSelf();

            // Before PowerUpSystem only for readability — PowerUpSystem takes it as a constructor
            // dependency, so the container orders the two itself.
            builder.Register<GhostFitSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PowerUpSystem>(Lifetime.Singleton).AsSelf();

            // Owns the currency slice of ProfileModel and is the only writer of it, so it is bound next
            // to the coin reward source it grants through. AsSelf because PowerUpShopView asks for the
            // concrete system — there is no second implementation to hide behind an interface.
            //
            // After PowerUpSystem for the same readability reason: it takes that system as a
            // constructor dependency (a coin purchase is a debit here and a grant there), so the
            // container orders the two itself either way.
            builder.Register<CurrencySystem>(Lifetime.Singleton).AsSelf();

            // Owns the ad-removal slice of ProfileModel and is the only writer of it. Its own System
            // rather than a fifth faucet on CurrencySystem: removing ads mints and spends no coin, so
            // it has no business inside the single writer of the balance. It shares that class's two
            // purchase seams though — one store integration, one validator, two products.
            //
            // AsSelf because SettingsPanelView asks for the concrete system — there is no second
            // implementation to hide behind an interface.
            builder.Register<AdRemovalSystem>(Lifetime.Singleton).AsSelf();

            builder.Register<PowerUpScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<ExplosiveCoreScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<LaserScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PiercingRocketScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<LaserSpawnSystem>(Lifetime.Singleton).AsSelf();

            // Watches the combo streak and asks BoardSystem to inject a golden 1x1 at the next refill.
            builder.Register<GoldenPieceTriggerSystem>(Lifetime.Singleton).AsSelf();

            // The third streak watcher: it converts an occupied cell into a coin cell at every streak
            // level from 2 to 6, worth 1/2/4/8/16 coins (issue #401). Its own System for the reason
            // LaserSpawnSystem is — the trigger is a score concept BoardSystem never reads.
            builder.Register<CoinStreakEscalationSystem>(Lifetime.Singleton).AsSelf();

            // Puts a level's authored coin cells on the board. Separate from LevelProgressionSystem,
            // which owns which level is played but deliberately never writes to the board.
            builder.Register<LevelCoinCellSeedSystem>(Lifetime.Singleton).AsSelf();

            // Puts a level's authored reinforced cells on the board. Unlike the coin seeder above it
            // subscribes to nothing: BoardSystem calls it inline while opening a run, because a
            // reinforced cell has to be standing before the tray is dealt (issue #153 AC1).
            builder.Register<LevelReinforcedCellSeeder>(Lifetime.Singleton).AsSelf();

            // Same shape and same reason as the reinforced-cell seeder immediately above: BoardSystem
            // calls it inline too, right after that one, so a timer cell is also standing before the
            // tray is dealt (issue #307 AC7/AC8).
            builder.Register<LevelTimerCellSeeder>(Lifetime.Singleton).AsSelf();

            // Rolls the diamond decoration of every dealt piece (issue #394). BoardSystem asks it per
            // dealt slot; it reads ObjectiveModel and GameModeModel live to gate itself to a Path run
            // with an active DiamondsCleared objective, so it needs no subscription of its own. The
            // spawn chance and decorated-count range come from the active level's first LevelCatalog
            // row via PathRunModel (issue #396), the same lookup the coin seeder and PowerUpSystem use.
            builder.Register<DiamondPieceDecorator>(Lifetime.Singleton).AsSelf();
            builder.Register<ObjectiveSystem>(Lifetime.Singleton);
            builder.Register<LevelProgressionSystem>(Lifetime.Singleton);
            builder.Register<BadgeStatsSystem>(Lifetime.Singleton);
            builder.Register<BadgeSystem>(Lifetime.Singleton);

            // Subscribes to all four of its triggers in its constructor, so it must be resolved eagerly
            // in the build callback below rather than waiting for a lazy resolve.
            builder.Register<InfoPopupSystem>(Lifetime.Singleton);

            // Entry point because it is an ITickable: the countdown is driven by VContainer's player
            // loop, not by a MonoBehaviour Update.
            builder.RegisterEntryPoint<TimerRunSystem>(Lifetime.Singleton).AsSelf();

            // Same reason: the 2x window has to expire on its own schedule, whether or not the player
            // places anything while it is open.
            builder.RegisterEntryPoint<DoubleMultiplierSystem>(Lifetime.Singleton).AsSelf();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Development-only manual test scenarios (F9: row+column cross-clear setup). Compiled out
            // of release builds entirely — see DebugCheatSystem.
            builder.Register<DebugCheatSystem>(Lifetime.Singleton);

            // Writes a plain-text log of the run in progress to persistentDataPath, overwritten at
            // every RunStartedMessage, so a tester can attach the file behind the most recently
            // played run to a bug report. Subscribes in its constructor, so it must be resolved
            // eagerly below rather than waiting for a lazy resolve — see the build callback.
            builder.Register<SessionRecorderSystem>(Lifetime.Singleton);
#endif
        }

        private static void RegisterViews(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardView>();
            builder.RegisterComponentInHierarchy<BackgroundView>();
            // The one persistent corner icon left. It opens the hub, which is the single modal owner of
            // the five cards below it — so those five have no icon of their own any more, only a tab.
            builder.RegisterComponentInHierarchy<SettingsButtonView>();
            builder.RegisterComponentInHierarchy<HubPanelView>();
            builder.RegisterComponentInHierarchy<SettingsPanelView>();
            builder.RegisterComponentInHierarchy<LevelPathButtonView>();
            builder.RegisterComponentInHierarchy<LevelPathPanelView>();
            builder.RegisterComponentInHierarchy<BadgesPanelView>();
            builder.RegisterComponentInHierarchy<ProfilePanelView>();
            builder.RegisterComponentInHierarchy<LeaderboardPanelView>();
            builder.RegisterComponentInHierarchy<PowerUpShopView>();

            // The level-start Coin Sower picker. Taken as a dependency by LevelPathPanelView, whose node
            // tap opens this instead of starting the run itself (see CoinSowerPickerView).
            builder.RegisterComponentInHierarchy<CoinSowerPickerView>();
            builder.RegisterComponentInHierarchy<PieceTrayView>();
            builder.RegisterComponentInHierarchy<HoldSlotView>();
            builder.RegisterComponentInHierarchy<ScoreView>();

            // Worn on the corner of LevelPathButtonView (registered above), which it takes as a dependency.
            builder.RegisterComponentInHierarchy<PathLevelBadgeView>();
            builder.RegisterComponentInHierarchy<TimerHudView>();

            // The combo streak pill (issue #265). Takes ScoreView and ObjectiveIconContainerView as
            // dependencies: it sits in the score card's centre slot, or at the goal row's end when the
            // timer has the slot.
            builder.RegisterComponentInHierarchy<StreakPillView>();
            builder.RegisterComponentInHierarchy<DoubleMultiplierHudView>();
            builder.RegisterComponentInHierarchy<CoinTotalHudView>();
            builder.RegisterComponentInHierarchy<GhostFitView>();
            builder.RegisterComponentInHierarchy<LineClearBurstView>();
            builder.RegisterComponentInHierarchy<BonusFeedbackView>();
            builder.RegisterComponentInHierarchy<PowerUpInventoryView>();

            // #353: the grant flight and its per-kind sound. Registered after PowerUpInventoryView,
            // which the flight view takes as a dependency to resolve its landing slot.
            builder.RegisterComponentInHierarchy<PowerUpGrantAnimationView>();
            builder.RegisterComponentInHierarchy<PowerUpGrantSfxView>();
            builder.RegisterComponentInHierarchy<ObjectiveIconContainerView>();
            builder.RegisterComponentInHierarchy<ObjectiveInfoPopupView>();
            builder.RegisterComponentInHierarchy<InfoPopupView>();

            // The one end-of-run card (issue #266): result, badges and the restart / change mode /
            // next level actions together. Opens on GameOverMessage; BoardInputView routes every tap
            // into it while it is up and carries out the action it resolves.
            builder.RegisterComponentInHierarchy<RunResultView>();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Its own new GameObject rather than a scene entry: it exists only in editor/development
            // builds, so nothing has to be added to or removed from the shipped scene.
            //
            // Unlike RegisterComponentInHierarchy, RegisterComponentOnNewGameObject does not force its
            // own resolution — nothing else depends on an input adapter, so without this callback the
            // registration sits unused and the GameObject is never spawned. The force-resolve callback
            // mirrors the one RegisterComponentInHierarchy adds internally.
            builder.RegisterComponentOnNewGameObject<DebugCheatInputView>(
                Lifetime.Singleton, nameof(DebugCheatInputView));
            builder.RegisterBuildCallback(container => container.Resolve<DebugCheatInputView>());
#endif
        }
    }
}
