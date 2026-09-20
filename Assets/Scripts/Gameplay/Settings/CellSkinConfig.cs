using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// Tuning for the cosmetic cell-skin conversion (issue #324, sub-issue A): the score milestones that
    /// progressively unlock each of the four themes, the cadence conversions repeat at once every theme
    /// is unlocked, and how many cells one batch converts. Static config, so it lives in a
    /// ScriptableObject — the same reason <see cref="TimedModeConfig"/> does: retuning the schedule or
    /// batch size is an asset edit, never a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Cell Skin Config", fileName = "CellSkinConfig")]
    public sealed class CellSkinConfig : ScriptableObject
    {
        private const int THEME_COUNT = 4;

        private static readonly int[] FallbackUnlockThresholds = { 1000, 2000, 3000, 4000 };

        private const int FALLBACK_REPEAT_INTERVAL = 500;
        private const int FALLBACK_MAX_CELLS_PER_CONVERSION = 3;

        [Tooltip("The score this run must reach to unlock each theme, in MustyBlockBlast.Core.CellSkinKind " +
            "declaration order (Cake, Candy, Jelly, Fruit) — index 0 is Cake's unlock score, index 3 is " +
            "Fruit's. Must have exactly 4 entries, each strictly greater than the previous one.")]
        [SerializeField] private int[] _themeUnlockThresholds = { 1000, 2000, 3000, 4000 };

        [Tooltip("Once every theme is unlocked (the run's score has passed the last threshold above), " +
            "every further multiple of this score triggers one more conversion batch. Repeats for the " +
            "rest of the run.")]
        [FormerlySerializedAs("_scoreIntervalPoints")]
        [SerializeField] private int _repeatIntervalPoints = FALLBACK_REPEAT_INTERVAL;

        [Tooltip("At most this many currently-occupied, still-plain cells are converted per batch (fewer " +
            "if the board does not have this many).")]
        [SerializeField] private int _maxCellsPerConversion = FALLBACK_MAX_CELLS_PER_CONVERSION;

        /// <summary>The four unlock thresholds in <see cref="MustyBlockBlast.Core.CellSkinKind"/>
        /// declaration order. Falls back to the default schedule on a misconfigured asset (wrong length,
        /// or not strictly increasing) rather than letting a bad edit unlock every theme at once or lock
        /// one out for the whole run.</summary>
        public IReadOnlyList<int> ThemeUnlockThresholds
            => IsValidThresholdList(_themeUnlockThresholds) ? _themeUnlockThresholds : FallbackUnlockThresholds;

        /// <summary>Score interval that triggers a repeat conversion batch once every theme is unlocked.
        /// Falls back to a sane positive default on a misconfigured (zero or negative) asset, so the
        /// trigger can never divide by zero or fire on every single point scored.</summary>
        public int RepeatIntervalPoints
            => _repeatIntervalPoints > 0 ? _repeatIntervalPoints : FALLBACK_REPEAT_INTERVAL;

        /// <summary>Cells converted per batch, at most. Falls back to a sane positive default the same
        /// way <see cref="RepeatIntervalPoints"/> does.</summary>
        public int MaxCellsPerConversion
            => _maxCellsPerConversion > 0 ? _maxCellsPerConversion : FALLBACK_MAX_CELLS_PER_CONVERSION;

        private static bool IsValidThresholdList(int[] thresholds)
        {
            if (thresholds == null || thresholds.Length != THEME_COUNT)
            {
                return false;
            }

            for (int i = 1; i < thresholds.Length; i++)
            {
                if (thresholds[i] <= thresholds[i - 1])
                {
                    return false;
                }
            }

            return thresholds[0] > 0;
        }
    }
}
