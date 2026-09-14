using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Localization;
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
            // before the first run starts. SettingsSystem, SfxSystem and LocalizationSystem load their
            // persisted settings in their constructors, so they must exist before any View subscribes
            // to SettingsModel/SfxModel/LocalizationModel in Start(). PowerUpSystem loads the persisted
            // inventory in its constructor and PowerUpScoreSystem subscribes in its own, so neither may
            // wait for a first lazy resolve.
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<ScoreSystem>();
                container.Resolve<TimedHighScoreSystem>();
                container.Resolve<SettingsSystem>();
                container.Resolve<SfxSystem>();
                container.Resolve<LocalizationSystem>();
                container.Resolve<PowerUpSystem>();
                container.Resolve<PowerUpScoreSystem>();

                // Subscribes in its constructor, like the systems above. ObjectiveSystem reads the run
                // score from ScoreChangedMessage rather than ScoreModel directly, so unlike the others
                // its correctness does not depend on resolve order relative to ScoreSystem.
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
            builder.Register<SfxModel>(Lifetime.Singleton);
            builder.Register<SettingsModel>(Lifetime.Singleton);
            builder.Register<LocalizationModel>(Lifetime.Singleton);
            builder.Register<PowerUpModel>(Lifetime.Singleton);
            builder.Register<ObjectiveModel>(Lifetime.Singleton);
            builder.Register<LevelProgressionModel>(Lifetime.Singleton);
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
            builder.Register<SfxSystem>(Lifetime.Singleton).As<ISfxService>().AsSelf();
            builder.Register<MusicSystem>(Lifetime.Singleton).As<IMusicService>().AsSelf();
            builder.Register<SettingsSystem>(Lifetime.Singleton);
            builder.Register<UnityLocalizedStringSource>(Lifetime.Singleton).As<ILocalizedStringSource>();
            builder.Register<LocalizationSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BoardSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<GameModeSystem>(Lifetime.Singleton);
            builder.Register<TimedModeSystem>(Lifetime.Singleton);

            // Always-granting stub until a rewarded-ad SDK is wired up; swapping it is one line here.
            builder.Register<DeterministicRewardSource>(Lifetime.Singleton).As<IRewardSource>().AsSelf();
            builder.Register<PowerUpSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<PowerUpScoreSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<ObjectiveSystem>(Lifetime.Singleton);
            builder.Register<LevelProgressionSystem>(Lifetime.Singleton);
            builder.Register<BadgeStatsSystem>(Lifetime.Singleton);
            builder.Register<BadgeSystem>(Lifetime.Singleton);

            // Entry point because it is an ITickable: the countdown is driven by VContainer's player
            // loop, not by a MonoBehaviour Update.
            builder.RegisterEntryPoint<TimerRunSystem>(Lifetime.Singleton).AsSelf();
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
            builder.RegisterComponentInHierarchy<ScoreView>();
            builder.RegisterComponentInHierarchy<TimerHudView>();
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
