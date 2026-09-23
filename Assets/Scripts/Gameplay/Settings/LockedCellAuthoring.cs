using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored locked cell ("Kilitli Hücre", issue #434) on a level's board: where it stands and
    /// how many of its DISTINCT orthogonal neighbours must each be destroyed at least once before it
    /// unlocks into an ordinary empty cell.
    /// <para>
    /// Shaped after <see cref="ReinforcedCellAuthoring"/> deliberately, so the four kinds of authored
    /// board furniture are edited the same way in the Inspector. Like a reinforced or timer cell — and
    /// unlike an ice socket — a locked cell brings its own block and starts the run occupied (AC1),
    /// which is all it takes to keep a piece from ever being placed on it.
    /// </para>
    /// <para>
    /// No colour is authored, for the reason <see cref="ReinforcedCellAuthoring"/> authors none, and no
    /// visual skin either: the skin is rolled at random per instance at seed time (AC9, and explicitly
    /// out of scope to author), by <c>LevelLockedCellSeeder</c>.
    /// </para>
    /// <para>
    /// The threshold's numeric range is clamped here; the rule that it must not exceed the position's
    /// REAL orthogonal-neighbour count (AC5 — a corner has two, an edge three, a cell beside a hole
    /// fewer) is <see cref="LevelObjectiveConfig.IsValid"/>'s, because only the level knows its holes.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class LockedCellAuthoring
    {
        /// <summary>Fewest distinct neighbour clears a lock may need: one — the "almost open" lock that
        /// opens on the first neighbour cleared beside it.</summary>
        public const int MIN_UNLOCK_THRESHOLD = 1;

        /// <summary>Most distinct neighbour clears a lock may need (issue #434 AC2). Also the number of
        /// visual stages each skin is spread across — a lock authored with fewer starts partway down the
        /// same peel.</summary>
        public const int MAX_UNLOCK_THRESHOLD = 3;

        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        [Tooltip("How many DIFFERENT orthogonal neighbours (up/down/left/right) must each be cleared at "
            + "least once before this cell unlocks. 1-3, and never more than the cell really has "
            + "(a corner has 2, an edge 3, a cell beside a hole fewer).")]
        [SerializeField] private int _unlockThreshold = MIN_UNLOCK_THRESHOLD;

        /// <summary>Distinct neighbour clears this cell needs before it unlocks.</summary>
        public int UnlockThreshold => _unlockThreshold;

        internal GridPosition ToGridPosition() => new GridPosition(_x, _y);

#if UNITY_EDITOR
        /// <summary>Clamps the authored threshold into range, called by
        /// <see cref="LevelObjectiveConfig.ValidateInEditor"/> exactly as that method clamps its other
        /// authored entries. The neighbour-count rule (AC5) is deliberately NOT clamped here — silently
        /// lowering an authored threshold would be worse than naming the bad entry, which
        /// <see cref="LevelObjectiveConfig.IsValid"/> does.</summary>
        internal void ValidateInEditor()
        {
            _unlockThreshold = Mathf.Clamp(_unlockThreshold, MIN_UNLOCK_THRESHOLD, MAX_UNLOCK_THRESHOLD);
        }
#endif
    }
}
