using MessagePipe;
using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Systems;
using UnityEngine.SceneManagement;

namespace MustyBlockBlast.Presentation
{
    /// <summary>
    /// The mode-select scene's one System (issue #379): records the player's pick and hands over to
    /// gameplay. Pure C# — no MonoBehaviour, no View — and, like <see cref="SplashSystem"/>, the only
    /// thing in its scene that knows the next scene's name.
    /// <para>
    /// The pick itself is persisted by the Models and Systems the gameplay scene shares by name but
    /// not by instance: <see cref="GameModeModel.SelectMode"/> writes the mode,
    /// <see cref="TimedModeSystem.SelectDuration"/> the Klasik round length, and
    /// <see cref="LevelPathOpenRequestSystem.Request"/> the "open the level picker on boot" flag for
    /// Macera. The gameplay scene's own fresh instances read all three back on construction, so the
    /// hand-over is nothing more than a scene load.
    /// </para>
    /// <para>
    /// There is deliberately no way out of this scene other than a pick — no back, no dismiss, no
    /// skip: it exists so that every launch chooses a mode.
    /// </para>
    /// </summary>
    public sealed class ModeSelectSystem
    {
        private const string TARGET_SCENE_NAME = "SampleScene";

        private readonly GameModeModel _gameModeModel;
        private readonly TimedModeSystem _timedModeSystem;
        private readonly LevelPathOpenRequestSystem _levelPathOpenRequest;
        private readonly IPublisher<GameModeChosenMessage> _modeChosenPublisher;

        private bool _transitionStarted;

        public ModeSelectSystem(
            GameModeModel gameModeModel,
            TimedModeSystem timedModeSystem,
            LevelPathOpenRequestSystem levelPathOpenRequest,
            IPublisher<GameModeChosenMessage> modeChosenPublisher)
        {
            _gameModeModel = gameModeModel;
            _timedModeSystem = timedModeSystem;
            _levelPathOpenRequest = levelPathOpenRequest;
            _modeChosenPublisher = modeChosenPublisher;

            // The plates pre-highlight the last-played mode, so the Model has to hold it before the
            // View reads it in Start.
            _gameModeModel.LoadPersistedMode();
        }

        /// <summary>Şölen Modu: endless, with every special cell and power-up. Straight into gameplay.</summary>
        public void ChooseEndless() => Choose(GameMode.Endless);

        /// <summary>
        /// Klasik Mod at <paramref name="durationSeconds"/> — one of <see cref="TimedModeSystem.AvailableDurations"/>,
        /// including the endless "Sınırsız" entry. A length the config does not offer is ignored by
        /// <see cref="TimedModeSystem.SelectDuration"/>, in which case the run starts on whatever length
        /// was already selected. Straight into gameplay.
        /// </summary>
        public void ChooseTimed(float durationSeconds)
        {
            if (_transitionStarted)
            {
                return;
            }

            _timedModeSystem.SelectDuration(durationSeconds);
            Choose(GameMode.Timed);
        }

        /// <summary>Macera Modu: into gameplay with the level path picker opened on top, so the player
        /// still chooses which level to play.</summary>
        public void ChoosePath()
        {
            if (_transitionStarted)
            {
                return;
            }

            _levelPathOpenRequest.Request();
            Choose(GameMode.Path);
        }

        private void Choose(GameMode mode)
        {
            // A second tap while the load is already underway must not re-record anything: the first
            // pick is the one the player is about to play.
            if (_transitionStarted)
            {
                return;
            }

            _transitionStarted = true;
            _gameModeModel.SelectMode(mode);

            // Before the load starts: the curtain has to be on screen before the scene load and the
            // gameplay scope's construction stall the main thread, or it would only appear after them.
            _modeChosenPublisher.Publish(new GameModeChosenMessage(mode));

            // Fire-and-forget: the load destroys the scene that owns this System, so there is nothing
            // here to await it for. SampleScene's own boot picks the choice up from PlayerPrefs.
            SceneManager.LoadSceneAsync(TARGET_SCENE_NAME);
        }
    }
}
