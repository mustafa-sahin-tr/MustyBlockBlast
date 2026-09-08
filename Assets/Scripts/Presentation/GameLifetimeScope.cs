using MessagePipe;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Views;
using VContainer;
using VContainer.Unity;

namespace MustyBlockBlast.Presentation
{
    /// <summary>Single composition root for the gameplay scene. The only place bindings happen.</summary>
    public sealed class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            RegisterMessaging(builder);
            RegisterModels(builder);
            RegisterSystems(builder);
            RegisterViews(builder);

            // ScoreSystem subscribes in its constructor, so it must exist before the first run starts.
            builder.RegisterBuildCallback(container => container.Resolve<ScoreSystem>());
        }

        private static void RegisterMessaging(IContainerBuilder builder)
        {
            MessagePipeOptions options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<RunStartedMessage>(options);
            builder.RegisterMessageBroker<PiecePlacedMessage>(options);
            builder.RegisterMessageBroker<LinesClearedMessage>(options);
            builder.RegisterMessageBroker<ScoreChangedMessage>(options);
            builder.RegisterMessageBroker<GameOverMessage>(options);
        }

        private static void RegisterModels(IContainerBuilder builder)
        {
            builder.Register<BoardModel>(Lifetime.Singleton);
            builder.Register<TrayModel>(Lifetime.Singleton);
            builder.Register<ScoreModel>(Lifetime.Singleton);
        }

        private static void RegisterSystems(IContainerBuilder builder)
        {
            builder.Register<WeightedPieceDraw>(Lifetime.Singleton);
            builder.Register<ScoreSystem>(Lifetime.Singleton);
            builder.RegisterEntryPoint<BoardSystem>(Lifetime.Singleton).AsSelf();
        }

        private static void RegisterViews(IContainerBuilder builder)
        {
            builder.RegisterComponentInHierarchy<BoardView>();
            builder.RegisterComponentInHierarchy<PieceTrayView>();
            builder.RegisterComponentInHierarchy<ScoreView>();
            builder.RegisterComponentInHierarchy<GameOverView>();
            builder.RegisterComponentInHierarchy<BoardInputView>();
        }
    }
}
