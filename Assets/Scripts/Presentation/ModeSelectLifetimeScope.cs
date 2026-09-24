using System.Collections.Generic;
using MessagePipe;
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
    /// <summary>
    /// Composition root for the mode-select scene (issue #379). The only place bindings happen.
    /// <para>
    /// Self-contained like <see cref="SplashLifetimeScope"/>: the three scenes are loadable
    /// independently and share no container, so this one carries its own language and theme stacks.
    /// It deliberately registers no board — <c>BoardSystem</c>, the tray, the score, none of it — because
    /// there is nothing to play here; the mode and duration Models it does register are the same types
    /// gameplay uses, talking to the same PlayerPrefs keys, which is how a pick made here reaches the
    /// gameplay scene's own fresh instances.
    /// </para>
    /// </summary>
    public sealed class ModeSelectLifetimeScope : LifetimeScope
    {
        [Header("Settings")]
        [Tooltip("Selectable themes in display order, the same list GameLifetimeScope holds. The first "
            + "entry is the default (Yaz). Only read for colours here — this scene changes no setting.")]
        [SerializeField] private ThemeDefinition[] _availableThemes;

        [Tooltip("Selectable round lengths for Klasik mode. The same asset GameLifetimeScope holds — a "
            + "different ladder here would offer lengths gameplay then refuses.")]
        [SerializeField] private TimedModeConfig _timedModeConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterSettings(builder);
            RegisterLocalization(builder);
            RegisterMessaging(builder);
            RegisterModeSelection(builder);
            RegisterViews(builder);

            // SettingsSystem and LocalizationSystem load their persisted theme and language in their
            // constructors, so they must exist before any View subscribes to SettingsModel or
            // LocalizationModel in Start(). ModeSelectSystem restores the persisted mode into
            // GameModeModel the same way, for the plates' pre-highlight.
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<SettingsSystem>();
                container.Resolve<LocalizationSystem>();
                container.Resolve<ModeSelectSystem>();
            });
        }

        /// <summary>The theme stack, so the Klasik box and the mode buttons are painted in the player's
        /// own season rather than a fixed palette. The gradient behind them is the splash's, fixed.</summary>
        private void RegisterSettings(IContainerBuilder builder)
        {
            builder.RegisterInstance<IReadOnlyList<ThemeDefinition>>(_availableThemes ?? new ThemeDefinition[0]);
            builder.Register<SettingsModel>(Lifetime.Singleton);
            builder.Register<SettingsSystem>(Lifetime.Singleton);
        }

        private static void RegisterLocalization(IContainerBuilder builder)
        {
            builder.RegisterInstance<IReadOnlyList<LocaleDefinition>>(UnityLocaleCatalog.Build());
            builder.Register<LocalizationModel>(Lifetime.Singleton);
            builder.Register<UnityLocalizedStringSource>(Lifetime.Singleton).As<ILocalizedStringSource>();
            builder.Register<LocalizationSystem>(Lifetime.Singleton);
        }

        /// <summary>The one message this scene carries: the pick, from <see cref="ModeSelectSystem"/> to
        /// the loading curtain, published just before the gameplay load begins.</summary>
        private static void RegisterMessaging(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<GameModeChosenMessage>(options);
        }

        /// <summary>
        /// The pick itself: the mode Model, the Klasik length System with its config, the Macera
        /// "open the picker on boot" request, and the one System that records a choice and loads gameplay.
        /// </summary>
        private void RegisterModeSelection(IContainerBuilder builder)
        {
            builder.RegisterInstance(ResolveTimedModeConfig());
            builder.Register<GameModeModel>(Lifetime.Singleton);
            builder.Register<TimedModeModel>(Lifetime.Singleton);
            builder.Register<TimedModeSystem>(Lifetime.Singleton);
            builder.Register<LevelPathOpenRequestSystem>(Lifetime.Singleton);
            builder.Register<ModeSelectSystem>(Lifetime.Singleton);
        }

        private static void RegisterViews(IContainerBuilder builder)
        {
            // The splash's own fixed blue gradient rather than the theme-reactive gameplay background:
            // this screen follows the splash and is meant to read as a continuation of it.
            builder.RegisterComponentInHierarchy<SplashBackgroundView>();
            builder.RegisterComponentInHierarchy<ModeSelectPanelView>();
            builder.RegisterComponentInHierarchy<ModeSelectInputView>();

            // Dormant until a pick; then it outlives this scene to cover the gameplay load.
            builder.RegisterComponentInHierarchy<LoadingCurtainView>();
        }

        /// <summary>
        /// The config is a required scene reference, but registering a null instance fails deep
        /// inside the container with an opaque error. Falling back to a default-valued instance keeps
        /// the scene bootable and turns the mistake into one readable console line — the same shape
        /// as <see cref="GameLifetimeScope"/>'s.
        /// </summary>
        private TimedModeConfig ResolveTimedModeConfig()
        {
            if (_timedModeConfig != null)
            {
                return _timedModeConfig;
            }

            Debug.LogError(
                $"{nameof(ModeSelectLifetimeScope)} has no {nameof(TimedModeConfig)} assigned. " +
                "Klasik mode is falling back to the built-in default durations.", this);
            return ScriptableObject.CreateInstance<TimedModeConfig>();
        }
    }
}
