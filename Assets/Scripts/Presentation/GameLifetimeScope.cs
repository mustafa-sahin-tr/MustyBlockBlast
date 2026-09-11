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

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterMessaging(builder);
            RegisterModels(builder);
            RegisterSystems(builder);
            RegisterViews(builder);

            // ScoreSystem subscribes in its constructor, so it must exist before the first run starts.
            // SettingsSystem and SfxSystem load their persisted settings in their constructors, so
            // they must exist before any View subscribes to SettingsModel/SfxModel in Start().
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<ScoreSystem>();
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
        }

        // Instance method: the theme list is scene-configured on this MonoBehaviour.
        private void RegisterModels(IContainerBuilder builder)
        {
            builder.RegisterInstance<IReadOnlyList<ThemeDefinition>>(
                _availableThemes ?? new ThemeDefinition[0]);

            builder.Register<BoardModel>(Lifetime.Singleton);
            builder.Register<TrayModel>(Lifetime.Singleton);
            builder.Register<ScoreModel>(Lifetime.Singleton);
            builder.Register<GameModeModel>(Lifetime.Singleton);
            builder.Register<SfxModel>(Lifetime.Singleton);
            builder.Register<SettingsModel>(Lifetime.Singleton);
        }

        private static void RegisterSystems(IContainerBuilder builder)
        {
            builder.Register<WeightedPieceDraw>(Lifetime.Singleton);
            builder.Register<ScoreSystem>(Lifetime.Singleton);
            builder.Register<SfxSystem>(Lifetime.Singleton).As<ISfxService>().AsSelf();
            builder.Register<SettingsSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BoardSystem>(Lifetime.Singleton).AsSelf();
            builder.Register<GameModeSystem>(Lifetime.Singleton);
        }

        private static void RegisterViews(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardView>();
            builder.RegisterComponentInHierarchy<BackgroundView>();
            builder.RegisterComponentInHierarchy<SettingsButtonView>();
            builder.RegisterComponentInHierarchy<SettingsPanelView>();
            builder.RegisterComponentInHierarchy<PieceTrayView>();
            builder.RegisterComponentInHierarchy<ScoreView>();
            builder.RegisterComponentInHierarchy<LineClearBurstView>();
            builder.RegisterComponentInHierarchy<GameOverView>();
            builder.RegisterComponentInHierarchy<BoardInputView>();
            builder.RegisterComponentInHierarchy<SfxPlayerView>();
            builder.RegisterComponentInHierarchy<LineClearSfxView>();
        }
    }
}
