using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="TimedModeModel"/>: the round length a timed run will use. Purely a selection
    /// System — it never counts anything down, that is <see cref="TimerRunSystem"/>'s job.
    /// <para>
    /// Changing the selection mid-run is deliberately harmless: <see cref="TimerRunSystem"/> only
    /// reads it on a tray refill, so the run in progress keeps the length it started on.
    /// </para>
    /// </summary>
    public sealed class TimedModeSystem
    {
        private static readonly float[] EmptyDurations = new float[0];

        private readonly TimedModeModel _model;
        private readonly TimedModeConfig _config;

        public TimedModeSystem(TimedModeModel model, TimedModeConfig config)
        {
            _model = model;
            _config = config;

            // Seeded here rather than in the Model so the Model stays free of the config asset.
            _model.SelectedDurationSeconds.Value = config != null ? config.DefaultDurationSeconds : 0f;
        }

        /// <summary>Selectable round lengths in seconds, in display order. Never null.</summary>
        public IReadOnlyList<float> AvailableDurations => _config != null ? _config.DurationsSeconds : EmptyDurations;

        /// <summary>The chosen round length, for Views to subscribe to. Writes go through <see cref="SelectDuration"/>.</summary>
        public ReactiveProperty<float> SelectedDuration => _model.SelectedDurationSeconds;

        /// <summary>
        /// Selects a round length. Values that are not in <see cref="AvailableDurations"/> are ignored,
        /// so a stale UI can never put the model into a length the config no longer offers.
        /// </summary>
        public void SelectDuration(float seconds)
        {
            IReadOnlyList<float> durations = AvailableDurations;
            for (int durationIndex = 0; durationIndex < durations.Count; durationIndex++)
            {
                if (!Mathf.Approximately(durations[durationIndex], seconds))
                {
                    continue;
                }

                _model.SelectedDurationSeconds.Value = durations[durationIndex];
                return;
            }
        }
    }
}
