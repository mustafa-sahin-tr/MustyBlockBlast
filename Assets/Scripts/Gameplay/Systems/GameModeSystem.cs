using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="GameModeModel"/> inside the gameplay scene. Switching mode restarts the run,
    /// because the board state of the run in progress belongs to the mode it was started in.
    /// <para>
    /// Both modes restart through the same <see cref="BoardSystem.StartNewRun"/>; the timed rule set
    /// is layered on by <c>TimerRunSystem</c>, which watches this mode rather than being switched by it.
    /// </para>
    /// <para>
    /// The mode's persistence belongs to the Model (issue #379): this System only asks it to restore
    /// the last selection on construction and to record a new one on <see cref="SelectMode"/>. The
    /// restore does not itself trigger a run start — <c>BoardSystem</c>'s own <c>IStartable.Start</c>
    /// is what starts the single run at boot, already in the restored mode.
    /// </para>
    /// </summary>
    public sealed class GameModeSystem
    {
        private readonly GameModeModel _model;
        private readonly BoardSystem _boardSystem;

        public GameModeSystem(GameModeModel model, BoardSystem boardSystem)
        {
            _model = model;
            _boardSystem = boardSystem;

            _model.LoadPersistedMode();
        }

        /// <summary>Read-only view of the active mode, for Views to subscribe to.</summary>
        public ReactiveProperty<GameMode> CurrentMode => _model.CurrentMode;

        /// <summary>Whether the active mode's ruleset includes special cells and power-ups (issue #355).
        /// See <see cref="GameModeModel.ExtrasEnabled"/> — this is a plain passthrough for callers that
        /// already depend on this System rather than the Model.</summary>
        public bool ExtrasEnabled => _model.ExtrasEnabled;

        /// <summary>Switches mode, persists it and restarts the run. Re-selecting the active mode does nothing.</summary>
        public void SelectMode(GameMode mode)
        {
            if (!_model.SelectMode(mode))
            {
                return;
            }

            _boardSystem.StartNewRun();
        }

        /// <summary>
        /// Restarts the run in the active mode, without changing or persisting the mode itself. Used
        /// when a mode's own settings change in a way that should apply to the run right away — e.g. a
        /// timed round's length — rather than waiting for the next natural restart.
        /// </summary>
        public void RestartRun() => _boardSystem.StartNewRun();
    }
}
