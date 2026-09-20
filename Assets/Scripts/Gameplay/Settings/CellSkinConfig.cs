using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// Tuning for the cosmetic cell-skin conversion (issue #324, sub-issue A): how often a Classic-mode
    /// run's score triggers a conversion batch, and how many cells one batch converts. Static config, so
    /// it lives in a ScriptableObject — the same reason <see cref="TimedModeConfig"/> does: retuning the
    /// cadence or batch size is an asset edit, never a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Cell Skin Config", fileName = "CellSkinConfig")]
    public sealed class CellSkinConfig : ScriptableObject
    {
        private const int FALLBACK_SCORE_INTERVAL = 500;
        private const int FALLBACK_MAX_CELLS_PER_CONVERSION = 3;

        [Tooltip("Every multiple of this score the run crosses triggers one conversion batch. Repeats " +
            "for the whole run, not a one-shot.")]
        [SerializeField] private int _scoreIntervalPoints = FALLBACK_SCORE_INTERVAL;

        [Tooltip("At most this many currently-occupied, still-plain cells are converted per batch (fewer " +
            "if the board does not have this many).")]
        [SerializeField] private int _maxCellsPerConversion = FALLBACK_MAX_CELLS_PER_CONVERSION;

        /// <summary>Score interval that triggers a conversion batch. Falls back to a sane positive
        /// default on a misconfigured (zero or negative) asset, so the trigger can never divide by zero
        /// or fire on every single point scored.</summary>
        public int ScoreIntervalPoints => _scoreIntervalPoints > 0 ? _scoreIntervalPoints : FALLBACK_SCORE_INTERVAL;

        /// <summary>Cells converted per batch, at most. Falls back to a sane positive default the same
        /// way <see cref="ScoreIntervalPoints"/> does.</summary>
        public int MaxCellsPerConversion
            => _maxCellsPerConversion > 0 ? _maxCellsPerConversion : FALLBACK_MAX_CELLS_PER_CONVERSION;
    }
}
