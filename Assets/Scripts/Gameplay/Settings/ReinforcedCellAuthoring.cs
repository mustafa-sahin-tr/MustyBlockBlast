using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored reinforced cell on a level's board: where it stands and how many hits it absorbs
    /// before it can be cleared away.
    /// <para>
    /// Exists for the reason <see cref="BoardHoleCell"/> does — <see cref="GridPosition"/> is a Core
    /// type Unity cannot serialize — and is shaped after it deliberately, so the two kinds of authored
    /// board furniture are edited the same way in the Inspector.
    /// </para>
    /// <para>
    /// No colour is authored: colour is cosmetic everywhere in this game and never affects placement or
    /// clearing, so the seeder draws one from the same palette ordinary pieces come from rather than
    /// making every level pick one.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class ReinforcedCellAuthoring
    {
        /// <summary>Fewest hits a reinforced cell may be authored with. Below two it would be an
        /// ordinary block that happens to start on the board, which needs none of this machinery.</summary>
        public const int MIN_HIT_COUNT = 2;

        /// <summary>Most hits a reinforced cell may be authored with (issue #153 AC1). Also the
        /// reference ceiling the View's damage stages are spread across.</summary>
        public const int MAX_HIT_COUNT = 4;

        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        [Tooltip("Hits this cell absorbs before a clear can remove it. 2-4.")]
        [SerializeField] private int _hitCount = MIN_HIT_COUNT;

        /// <summary>Hits this cell starts the level with.</summary>
        public int HitCount => _hitCount;

        internal GridPosition ToGridPosition() => new GridPosition(_x, _y);

#if UNITY_EDITOR
        /// <summary>Clamps the authored hit count into range, called by
        /// <see cref="LevelObjectiveConfig.ValidateInEditor"/> exactly as that method clamps its own
        /// numeric fields.</summary>
        internal void ValidateInEditor()
        {
            _hitCount = Mathf.Clamp(_hitCount, MIN_HIT_COUNT, MAX_HIT_COUNT);
        }
#endif
    }
}
