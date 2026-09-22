using MustyBlockBlast.Gameplay.Systems;
using MustyBlockBlast.Presentation.Views;
using VContainer.Unity;

namespace MustyBlockBlast.Presentation
{
    /// <summary>
    /// Boot System for the gameplay scene (issue #379): if the mode-select scene asked for it — the
    /// player picked Macera Modu, which is played one chosen level at a time — opens the level path
    /// picker over the freshly started run so the player still picks their level, exactly as a tap on
    /// the HUD's path icon would. Consumes the request, so a later plain boot never re-opens it.
    /// <para>
    /// Presentation-side rather than Gameplay because the thing it opens is a View. It holds no logic
    /// of its own: whether a request is pending is <see cref="LevelPathOpenRequestSystem"/>'s answer,
    /// and <see cref="LevelPathPanelView.Open"/> copes with being asked before its card is built, so
    /// the relative order of this entry point and the View's Start does not matter.
    /// </para>
    /// </summary>
    public sealed class PendingLevelPathOpenSystem : IStartable
    {
        private readonly LevelPathOpenRequestSystem _openRequest;
        private readonly LevelPathPanelView _levelPathPanelView;

        public PendingLevelPathOpenSystem(LevelPathOpenRequestSystem openRequest, LevelPathPanelView levelPathPanelView)
        {
            _openRequest = openRequest;
            _levelPathPanelView = levelPathPanelView;
        }

        public void Start()
        {
            if (!_openRequest.TryConsume())
            {
                return;
            }

            _levelPathPanelView.Open();
        }
    }
}
