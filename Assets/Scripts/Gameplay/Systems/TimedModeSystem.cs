using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Models;
using Mtafasahin.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="TimedModeModel"/>: the round length a timed run will use. Purely a selection
    /// System — it never counts anything down, that is <see cref="TimerRunSystem"/>'s job.
    /// <para>
    /// Changing the selection mid-run is deliberately harmless: <see cref="TimerRunSystem"/> only
    /// reads it once, on run start, so the run in progress keeps the length it started on.
    /// </para>
    /// <para>
    /// Persists the selection (issue #379): the mode-select scene picks a length in its own container,
    /// which is torn down the instant gameplay loads, so the choice has to survive in PlayerPrefs for
    /// the gameplay scene's fresh <see cref="TimedModeModel"/> to pick up. Restored on construction,
    /// the same way <see cref="GameModeSystem"/> restores the mode; a saved value the config no longer
    /// offers falls back to the config's default rather than putting the model into a dead length.
    /// </para>
    /// </summary>
    public sealed class TimedModeSystem
    {
        private const string TIMED_DURATION_PREFS_KEY = "Settings.TimedDurationSeconds";

        private static readonly float[] EmptyDurations = new float[0];

        private readonly TimedModeModel _model;
        private readonly TimedModeConfig _config;

        public TimedModeSystem(TimedModeModel model, TimedModeConfig config)
        {
            _model = model;
            _config = config;

            // Seeded here rather than in the Model so the Model stays free of both the config asset
            // and PlayerPrefs.
            float defaultSeconds = config != null ? config.DefaultDurationSeconds : 0f;
            float savedSeconds = PlayerPrefs.GetFloat(TIMED_DURATION_PREFS_KEY, defaultSeconds);
            _model.SelectedDurationSeconds.Value = TryResolveOffered(savedSeconds, out float offered)
                ? offered
                : defaultSeconds;
        }

        /// <summary>Selectable round lengths in seconds, in display order. Never null.</summary>
        public IReadOnlyList<float> AvailableDurations => _config != null ? _config.DurationsSeconds : EmptyDurations;

        /// <summary>The chosen round length, for Views to subscribe to. Writes go through <see cref="SelectDuration"/>.</summary>
        public ReactiveProperty<float> SelectedDuration => _model.SelectedDurationSeconds;

        /// <summary>
        /// Selects and persists a round length. Values that are not in <see cref="AvailableDurations"/>
        /// are ignored, so a stale UI can never put the model into a length the config no longer offers.
        /// </summary>
        public void SelectDuration(float seconds)
        {
            if (!TryResolveOffered(seconds, out float offered))
            {
                return;
            }

            _model.SelectedDurationSeconds.Value = offered;
            PlayerPrefs.SetFloat(TIMED_DURATION_PREFS_KEY, offered);
        }

        /// <summary>
        /// Maps a requested length onto the config's own entry for it — the canonical float, not the
        /// approximately-equal one that was asked for — or reports that nothing in the ladder matches.
        /// </summary>
        private bool TryResolveOffered(float seconds, out float offered)
        {
            IReadOnlyList<float> durations = AvailableDurations;
            for (int durationIndex = 0; durationIndex < durations.Count; durationIndex++)
            {
                if (!Mathf.Approximately(durations[durationIndex], seconds))
                {
                    continue;
                }

                offered = durations[durationIndex];
                return true;
            }

            offered = 0f;
            return false;
        }
    }
}
