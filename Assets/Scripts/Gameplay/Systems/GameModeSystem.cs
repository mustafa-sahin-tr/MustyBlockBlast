using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="GameModeModel"/>. Switching mode restarts the run, because the board state of
    /// the run in progress belongs to the mode it was started in.
    /// <para>
    /// Both modes restart through the same <see cref="BoardSystem.StartNewRun"/>; the timed rule set
    /// is layered on by <c>TimerRunSystem</c>, which watches this mode rather than being switched by it.
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
        }

        /// <summary>Read-only view of the active mode, for Views to subscribe to.</summary>
        public ReactiveProperty<GameMode> CurrentMode => _model.CurrentMode;

        /// <summary>Switches mode and restarts the run. Re-selecting the active mode does nothing.</summary>
        public void SelectMode(GameMode mode)
        {
            if (mode == _model.CurrentMode.Value)
            {
                return;
            }

            _model.CurrentMode.Value = mode;
            _boardSystem.StartNewRun();
        }
    }
}
