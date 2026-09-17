using System;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="GameModeModel"/>. Switching mode restarts the run, because the board state of
    /// the run in progress belongs to the mode it was started in.
    /// <para>
    /// Both modes restart through the same <see cref="BoardSystem.StartNewRun"/>; the timed rule set
    /// is layered on by <c>TimerRunSystem</c>, which watches this mode rather than being switched by it.
    /// </para>
    /// <para>
    /// Loads the last-selected mode from PlayerPrefs on construction and persists it on every
    /// <see cref="SelectMode"/> call, mirroring <see cref="SettingsSystem"/>'s theme persistence. The
    /// loaded value is written directly into the model rather than through <see cref="SelectMode"/>, so
    /// it does not itself trigger a run start — <c>BoardSystem</c>'s own <c>IStartable.Start</c> is what
    /// starts the single run at boot, already in the restored mode.
    /// </para>
    /// </summary>
    public sealed class GameModeSystem
    {
        private const string GAME_MODE_PREFS_KEY = "Settings.GameMode";

        private readonly GameModeModel _model;
        private readonly BoardSystem _boardSystem;

        public GameModeSystem(GameModeModel model, BoardSystem boardSystem)
        {
            _model = model;
            _boardSystem = boardSystem;

            int savedMode = PlayerPrefs.GetInt(GAME_MODE_PREFS_KEY, (int)GameMode.Endless);
            _model.CurrentMode.Value = Enum.IsDefined(typeof(GameMode), savedMode)
                ? (GameMode)savedMode
                : GameMode.Endless;
        }

        /// <summary>Read-only view of the active mode, for Views to subscribe to.</summary>
        public ReactiveProperty<GameMode> CurrentMode => _model.CurrentMode;

        /// <summary>Switches mode, persists it and restarts the run. Re-selecting the active mode does nothing.</summary>
        public void SelectMode(GameMode mode)
        {
            if (mode == _model.CurrentMode.Value)
            {
                return;
            }

            _model.CurrentMode.Value = mode;
            PlayerPrefs.SetInt(GAME_MODE_PREFS_KEY, (int)mode);
            _boardSystem.StartNewRun();
        }
    }
}
