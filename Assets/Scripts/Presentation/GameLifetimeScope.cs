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

                // Subscribes in its constructor, like PowerUpScoreSystem: it must be listening before
                // the first placement can detonate a core, not be constructed by one.
                container.Resolve<ExplosiveCoreScoreSystem>();

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
            builder.RegisterMessageBroker<ObjectiveProgressChangedMessage>(options);
            builder.RegisterMessageBroker<ObjectiveCompletedMessage>(options);
            builder.RegisterMessageBroker<LevelAdvancedMessage>(options);
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

            // Languages come from the project's Locale assets rather than a scene field: a new
            // language is a Locale asset plus a String Table column, with no scene edit.
            builder.RegisterInstance<IReadOnlyList<LocaleDefinition>>(UnityLocaleCatalog.Build());

            builder.Register<BoardModel>(Lifetime.Singleton);
            builder.Register<TrayModel>(Lifetime.Singleton);
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

            // Always-granting stub until a rewarded-ad SDK is wired up; swapping it is one line here.
            builder.Register<DeterministicRewardSource>(Lifetime.Singleton).As<IRewardSource>().AsSelf();
            // Before PowerUpSystem only for readability — PowerUpSystem takes it as a constructor
            // dependency, so the container orders the two itself.
            builder.Register<GhostFitSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PowerUpSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PowerUpScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<ExplosiveCoreScoreSystem>(Lifetime.Singleton).AsSelf();
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
            builder.RegisterComponentInHierarchy<PieceTrayView>();
            builder.RegisterComponentInHierarchy<HoldSlotView>();
            builder.RegisterComponentInHierarchy<ScoreView>();
            builder.RegisterComponentInHierarchy<TimerHudView>();
            builder.RegisterComponentInHierarchy<DoubleMultiplierHudView>();
            builder.RegisterComponentInHierarchy<GhostFitView>();
            builder.RegisterComponentInHierarchy<LineClearBurstView>();
            builder.RegisterComponentInHierarchy<BonusFeedbackView>();
            builder.RegisterComponentInHierarchy<PowerUpInventoryView>();
            builder.RegisterComponentInHierarchy<ObjectiveHudView>();
            builder.RegisterComponentInHierarchy<GameOverView>();
            builder.RegisterComponentInHierarchy<BoardInputView>();
            builder.RegisterComponentInHierarchy<SfxPlayerView>();
            builder.RegisterComponentInHierarchy<MusicPlayerView>();
            builder.RegisterComponentInHierarchy<GameMusicView>();
            builder.RegisterComponentInHierarchy<LineClearSfxView>();
            builder.RegisterComponentInHierarchy<BlockPlaceSfxView>();
            builder.RegisterComponentInHierarchy<GameOverSfxView>();
            builder.RegisterComponentInHierarchy<NewRecordSfxView>();
            builder.RegisterComponentInHierarchy<BonusSfxView>();
        }
    }
}
