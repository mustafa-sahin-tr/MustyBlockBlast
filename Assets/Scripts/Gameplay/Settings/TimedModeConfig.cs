using System.Collections.Generic;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The selectable round durations for <see cref="GameMode.Timed"/>. Static config, so it lives in
    /// a ScriptableObject: retuning the ladder (adding a 30s entry, dropping the 5s one) is an asset
    /// edit, never a code change.
    /// <para>
    /// One asset, referenced by the scene's LifetimeScope. The chosen value is runtime state and
    /// lives in <c>TimedModeModel</c> instead.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Timed Mode Config", fileName = "TimedModeConfig")]
    public sealed class TimedModeConfig : ScriptableObject
    {
        /// <summary>Used when the asset is misconfigured (empty list), so a run is never un-timeable.</summary>
        private const float FALLBACK_DURATION_SECONDS = 15f;

        private static readonly float[] EmptyDurations = new float[0];

        [Header("Durations")]
        [Tooltip("Selectable round lengths in seconds, in display order.")]
        [SerializeField] private float[] _durationsSeconds = { 5f, 10f, 15f, 20f, 25f };

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
    }
}
