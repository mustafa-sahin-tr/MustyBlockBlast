using System;
using Mtafasahin.Reactive;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The mode the current run is being played under, and the owner of its persistence (issue #379).
    /// A fresh instance boots endless; <see cref="LoadPersistedMode"/> restores the last selection and
    /// <see cref="SelectMode"/> records a new one. Both live here rather than in
    /// <c>GameModeSystem</c> so the mode-select scene — which has no board and therefore no
    /// <c>BoardSystem</c> for that System to depend on — can read and write the very same choice.
    /// </summary>
    public sealed class GameModeModel
    {
        private const string GAME_MODE_PREFS_KEY = "Settings.GameMode";

        public ReactiveProperty<GameMode> CurrentMode { get; } =
            new ReactiveProperty<GameMode>(GameMode.Endless);

        /// <summary>
        /// Whether special cells and power-ups are part of the current mode's ruleset (issue #355).
        /// True for every mode except <see cref="GameMode.Timed"/> — the enum member kept its ordinal
        /// and its identifier for save-data compatibility, but it is now "Classic" (Turkish: "Klasik
        /// Mod"): the one mode with no special cells and no power-ups, whatever duration (including the
        /// endless one) it is played with.
        /// <para>
        /// The single source of truth every special-cell spawn and every power-up arm/use/grant path
        /// reads instead of comparing <see cref="CurrentMode"/> to <see cref="GameMode.Timed"/> itself,
        /// so a future mode that also wants the no-extras ruleset only has to change this one line.
        /// </para>
        /// </summary>
        public bool ExtrasEnabled => CurrentMode.Value != GameMode.Timed;

        /// <summary>
        /// Restores the last-selected mode from PlayerPrefs. An unset or corrupted value falls back to
        /// <see cref="GameMode.Endless"/>. Written straight into <see cref="CurrentMode"/> rather than
        /// through <see cref="SelectMode"/>: restoring is not a selection, and must not re-persist or be
        /// mistaken by a caller for a change worth restarting a run over.
        /// </summary>
        public void LoadPersistedMode()
        {
            int savedMode = PlayerPrefs.GetInt(GAME_MODE_PREFS_KEY, (int)GameMode.Endless);
            CurrentMode.Value = Enum.IsDefined(typeof(GameMode), savedMode)
                ? (GameMode)savedMode
                : GameMode.Endless;
        }

        /// <summary>
        /// Switches mode and persists it. Returns false, and touches nothing, when <paramref name="mode"/>
        /// is already the active one — so a caller that restarts a run on a switch can skip the restart.
        /// </summary>
        public bool SelectMode(GameMode mode)
        {
            if (mode == CurrentMode.Value)
            {
                return false;
            }

            CurrentMode.Value = mode;
            PlayerPrefs.SetInt(GAME_MODE_PREFS_KEY, (int)mode);
            return true;
        }
    }
}
