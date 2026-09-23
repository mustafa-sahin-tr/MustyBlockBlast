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

        /// <summary>Most hits a reinforced cell may be authored with. Was 4 (issue #153 AC1); dropped to
        /// 3 by issue #438, when the reinforced cell took over the locked cell's skin art: that art has
        /// exactly <c>UiSpriteFactory.LOCKED_SKIN_STAGES</c> (3) "layers remaining" stages, and a hit
        /// count that maps 1:1 onto them means every hit visibly peels one layer with no fourth stage
        /// to draw. No level had authored 4 when the ceiling moved, so nothing was re-clamped.</summary>
        public const int MAX_HIT_COUNT = 3;

        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        [Tooltip("Hits this cell absorbs before a clear can remove it. 2-3.")]
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
