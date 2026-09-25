using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The numbers around a "score within N moves" level (issue #465): how many moves the one rewarded
    /// ad adds when the budget runs out, what each move left over pays when the target is reached, and
    /// from how many moves left the HUD counter turns to its warning colour. The budget itself is
    /// authored per level (<see cref="LevelObjectiveConfig.MoveLimit"/>); these are the same for every
    /// level, so they live in one ScriptableObject for the reason <see cref="LivesConfig"/> does —
    /// retuning them is an asset edit, never a code change.
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Move Budget Config", fileName = "MoveBudgetConfig")]
    public sealed class MoveBudgetConfig : ScriptableObject
    {
        [Tooltip("Moves the rewarded ad adds when the budget runs out before the target (two full docks' "
            + "worth by default). Offered at most once per level attempt.")]
        [SerializeField] private int _extraMovesFromAd = 6;

        [Tooltip("Points each move left over pays when the target is reached. Added to the run score, so "
            + "it counts toward the high score.")]
        [SerializeField] private int _bonusPerLeftoverMove = 20;

        [Tooltip("The HUD counter turns to its warning colour at this many moves left or fewer.")]
        [SerializeField] private int _lowMovesThreshold = 3;

        /// <summary>Moves one watched ad adds. At least 1: an ad that adds nothing is not an offer.</summary>
        public int ExtraMovesFromAd => Mathf.Max(1, _extraMovesFromAd);

        /// <summary>Points per leftover move. Never negative — a bonus must never cost the player.</summary>
        public int BonusPerLeftoverMove => Mathf.Max(0, _bonusPerLeftoverMove);

        /// <summary>Moves left at or below which the counter warns. Never negative.</summary>
        public int LowMovesThreshold => Mathf.Max(0, _lowMovesThreshold);
    }
}
