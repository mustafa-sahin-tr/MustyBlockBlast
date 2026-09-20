using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The selectable round durations for <see cref="GameMode.Timed"/> (displayed as "Klasik Mod" /
    /// Classic — issue #355). Static config, so it lives in a ScriptableObject: retuning the ladder
    /// (adding a 30s entry, dropping the 5s one) is an asset edit, never a code change.
    /// <para>
    /// One asset, referenced by the scene's LifetimeScope. The chosen value is runtime state and
    /// lives in <c>TimedModeModel</c> instead.
    /// </para>
    /// <para>
    /// <see cref="ENDLESS_DURATION_SECONDS"/> is a real, selectable entry in the ladder — the picker's
    /// "Sınırsız" option — rather than a value outside it: it is exactly as legitimate a choice as
    /// 180f/300f/600f, it is just the one that never counts down (see <see cref="IsEndlessDuration"/>,
    /// read by <c>TimerRunSystem</c>, <c>ScoreView</c> and <c>RunResultView</c>).
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Timed Mode Config", fileName = "TimedModeConfig")]
    public sealed class TimedModeConfig : ScriptableObject
    {
        /// <summary>
        /// The sentinel duration meaning "no countdown, play forever" — Classic mode's "Sınırsız"
        /// entry (issue #355). Zero rather than a negative or <see cref="float.PositiveInfinity"/>
        /// value: every consumer that ever divides or formats a duration (the minutes caption, the
        /// mm:ss countdown) treats zero safely, and <see cref="TimedHighScoreKey.For"/> already rounds
        /// it to a whole-seconds key distinct from every other duration's without any change of its own.
        /// </summary>
        public const float ENDLESS_DURATION_SECONDS = 0f;

        /// <summary>Used when the asset is misconfigured (empty list), so a run is never un-timeable.</summary>
        private const float FALLBACK_DURATION_SECONDS = 15f;

        private static readonly float[] EmptyDurations = new float[0];

        [Header("Durations")]
        [Tooltip("Selectable round lengths in seconds, in display order. 0 is the \"Sınırsız\" "
            + "(endless/no countdown) entry.")]
        [SerializeField] private float[] _durationsSeconds = { 0f, 180f, 300f, 600f };

        [Tooltip("Index into the list above that a fresh boot starts on. Clamped, so it is safe to shorten the list.")]
        [SerializeField] private int _defaultDurationIndex = 2;

        /// <summary>Selectable round lengths in seconds, in display order. Never null.</summary>
        public IReadOnlyList<float> DurationsSeconds => _durationsSeconds ?? EmptyDurations;

        /// <summary>The duration a fresh boot starts on. Falls back sanely on a misconfigured asset.</summary>
        public float DefaultDurationSeconds
        {
            get
            {
                if (_durationsSeconds == null || _durationsSeconds.Length == 0)
                {
                    return FALLBACK_DURATION_SECONDS;
                }

                int index = Mathf.Clamp(_defaultDurationIndex, 0, _durationsSeconds.Length - 1);
                return _durationsSeconds[index];
            }
        }

        /// <summary>True for <see cref="ENDLESS_DURATION_SECONDS"/> — Classic mode's "Sınırsız" entry
        /// — and false for every real, counted-down length. The one place this comparison is made, so
        /// every reader (<c>TimerRunSystem</c>, <c>ScoreView</c>, <c>RunResultView</c>,
        /// <c>SettingsPanelView</c>) agrees on what "no countdown" means.</summary>
        public static bool IsEndlessDuration(float durationSeconds) => durationSeconds <= ENDLESS_DURATION_SECONDS;
    }
}
