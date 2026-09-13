using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Gameplay.Localization;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Localization;
using MustyBlockBlast.Presentation.Views;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MustyBlockBlast.Presentation
{
    /// <summary>Composition root for the splash scene. The only place bindings happen.</summary>
    public sealed class SplashLifetimeScope : LifetimeScope
    {
        [Header("Settings")]
        [Tooltip("Placeholder splash duration before transitioning to gameplay.")]
        [Min(0f)]
        [SerializeField] private float _splashDurationSeconds = 2f;

        protected override void Configure(IContainerBuilder builder)
        {
            RegisterMessaging(builder);
            RegisterAudio(builder);
            RegisterLocalization(builder);
            RegisterSplash(builder);
            RegisterDecor(builder);

            // SfxSystem loads the persisted mute flag in its constructor, so it must exist before
            // SplashView asks it to play anything in Start(); LocalizationSystem loads the persisted
            // language the same way, before SplashCaptionView reads its captions in Start().
            builder.RegisterBuildCallback(container =>
            {
                container.Resolve<SfxSystem>();
                container.Resolve<LocalizationSystem>();
            });
        }

        /// <summary>
        /// The splash screen has captions of its own, so it needs the same language stack the gameplay
        /// scene has — the two scenes are loadable independently and share no container.
        /// </summary>
        private static void RegisterLocalization(IContainerBuilder builder)
        {
            builder.RegisterInstance<IReadOnlyList<LocaleDefinition>>(UnityLocaleCatalog.Build());
            builder.Register<LocalizationModel>(Lifetime.Singleton);
            builder.Register<UnityLocalizedStringSource>(Lifetime.Singleton).As<ILocalizedStringSource>();
            builder.Register<LocalizationSystem>(Lifetime.Singleton);
        }

        private static void RegisterMessaging(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<PlaySfxRequestedMessage>(options);
        }

        private static void RegisterAudio(IContainerBuilder builder)
        {
            builder.Register<SfxModel>(Lifetime.Singleton);
            builder.Register<SfxSystem>(Lifetime.Singleton).As<ISfxService>().AsSelf();

            builder.RegisterComponentInHierarchy<SfxPlayerView>();
            builder.RegisterComponentInHierarchy<SplashView>();
        }

        private void RegisterSplash(IContainerBuilder builder)
        {
            // Built by factory rather than registering the duration as a bare float: a primitive
            // binding is resolved by type alone, so any future float registration in this scope
            // would silently collide with it.
            builder.RegisterEntryPoint<SplashSystem>(
                    _ => new SplashSystem(_splashDurationSeconds), Lifetime.Singleton)
                .AsSelf();

            builder.RegisterComponentInHierarchy<SplashInputView>();
        }

        /// <summary>
        /// Non-interactive Views. Most take no dependencies at all (registering them anyway keeps every
        /// splash-scene View discoverable from the one composition root); the caption cluster is the
        /// exception, since its text is localized.
        /// </summary>
        private static void RegisterDecor(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<SplashBackgroundView>();
            builder.RegisterComponentInHierarchy<SplashFallingPiecesView>();
            builder.RegisterComponentInHierarchy<SplashCaptionView>();
        }
    }
}
