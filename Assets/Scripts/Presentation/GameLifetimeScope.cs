using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using MustyBlockBlast.Gameplay.Systems;
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

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterMessaging(builder);
            RegisterModels(builder);
            RegisterSystems(builder);
            RegisterViews(builder);

            // ScoreSystem and TimedHighScoreSystem subscribe in their constructors, so they must exist
            // before the first run starts. SettingsSystem and SfxSystem load their persisted settings
            // in their constructors, so they must exist before any View subscribes to
            // SettingsModel/SfxModel in Start().
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<ScoreSystem>();
                container.Resolve<TimedHighScoreSystem>();
                container.Resolve<SettingsSystem>();
                container.Resolve<SfxSystem>();
            });
        }

        private static void RegisterMessaging(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<RunStartedMessage>(options);
            builder.RegisterMessageBroker<PiecePlacedMessage>(options);
            builder.RegisterMessageBroker<LinesClearedMessage>(options);
            builder.RegisterMessageBroker<ScoreChangedMessage>(options);
            builder.RegisterMessageBroker<GameOverMessage>(options);
            builder.RegisterMessageBroker<PlaySfxRequestedMessage>(options);
            builder.RegisterMessageBroker<TrayRefilledMessage>(options);
        }

        // Instance method: the theme list and the timed-mode config are scene-configured on this
        // MonoBehaviour.
        private void RegisterModels(IContainerBuilder builder)
        {
            builder.RegisterInstance<IReadOnlyList<ThemeDefinition>>(
                _availableThemes ?? new ThemeDefinition[0]);
            builder.RegisterInstance(ResolveTimedModeConfig());

            builder.Register<BoardModel>(Lifetime.Singleton);
            builder.Register<TrayModel>(Lifetime.Singleton);
            builder.Register<ScoreModel>(Lifetime.Singleton);
            builder.Register<TimedHighScoreModel>(Lifetime.Singleton);
            builder.Register<GameModeModel>(Lifetime.Singleton);
            builder.Register<TimedModeModel>(Lifetime.Singleton);
            builder.Register<TimerModel>(Lifetime.Singleton);
            builder.Register<SfxModel>(Lifetime.Singleton);
            builder.Register<SettingsModel>(Lifetime.Singleton);
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
            builder.Register<ScoreSystem>(Lifetime.Singleton);
            builder.Register<TimedHighScoreSystem>(Lifetime.Singleton);
            builder.Register<SfxSystem>(Lifetime.Singleton).As<ISfxService>().AsSelf();
            builder.Register<SettingsSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BoardSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<GameModeSystem>(Lifetime.Singleton);
            builder.Register<TimedModeSystem>(Lifetime.Singleton);

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
            builder.RegisterComponentInHierarchy<PieceTrayView>();
            builder.RegisterComponentInHierarchy<ScoreView>();
            builder.RegisterComponentInHierarchy<TimerHudView>();
            builder.RegisterComponentInHierarchy<LineClearBurstView>();
            builder.RegisterComponentInHierarchy<GameOverView>();
            builder.RegisterComponentInHierarchy<BoardInputView>();
            builder.RegisterComponentInHierarchy<SfxPlayerView>();
            builder.RegisterComponentInHierarchy<LineClearSfxView>();
        }
    }
}
