using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored ice socket ("Buzlu Hedef Hücre", issue #433) on a level's board: where it stands
    /// and how many levels of ice it starts with. The player fills the position with any ordinary
    /// piece, clears the line through it, and the ice melts by one level; when it reaches 0 the
    /// position is an ordinary cell again. The level's <see cref="ObjectiveType.IceCellsCleared"/>
    /// objective is complete when every socket authored here has melted to 0.
    /// <para>
    /// Shaped after <see cref="ReinforcedCellAuthoring"/> deliberately, so the three kinds of authored
    /// board furniture are edited the same way in the Inspector — but a sibling rather than an
    /// extension of it, because the two differ in the one way that matters: a reinforced cell brings
    /// its own block and starts the run occupied, whereas an ice socket starts the run EMPTY and
    /// playable (AC2). Only the ice marker is authored; the cell itself holds nothing until the player
    /// puts something there.
    /// </para>
    /// <para>
    /// No colour is authored, for the same reason <see cref="ReinforcedCellAuthoring"/> authors none —
    /// and here there is not even a block to colour: the seeder writes the marker and nothing else.
    /// </para>
    /// <para>
    /// <b>Level-design note:</b> do not combine an ice-target objective with a
    /// <see cref="ObjectiveType.DiamondsCleared"/> objective in the same level. A diamond never lands
    /// on a position with ice still to melt (<see cref="DiamondCellRules.CanCarryDiamond"/>, product
    /// decision), so every socket is a position the diamond decoration can never be collected from —
    /// enough of them and the diamond objective could starve. This is a guideline for whoever authors
    /// the level; nothing enforces it at runtime.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class TargetIceCellAuthoring
    {
        /// <summary>Fewest ice levels a socket may be authored with. One level is the smallest
        /// meaningful socket: fill it once, clear it once, done.</summary>
        public const int MIN_ICE_LEVEL = 1;

        /// <summary>Most ice levels a socket may be authored with. Also the reference ceiling the
        /// View's overlay opacity is spread across (a socket authored with fewer simply starts partway
        /// down the same ramp).</summary>
        public const int MAX_ICE_LEVEL = 3;

        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        [Tooltip("Ice levels this position starts with. Each clear of the block standing on it melts one. 1-3.")]
        [SerializeField] private int _iceLevel = MIN_ICE_LEVEL;

        /// <summary>Ice levels this position starts the level with.</summary>
        public int IceLevel => _iceLevel;

        internal GridPosition ToGridPosition() => new GridPosition(_x, _y);

#if UNITY_EDITOR
        /// <summary>Clamps the authored ice level into range, called by
        /// <see cref="LevelObjectiveConfig.ValidateInEditor"/> exactly as that method clamps its other
        /// authored entries.</summary>
        internal void ValidateInEditor()
        {
            _iceLevel = Mathf.Clamp(_iceLevel, MIN_ICE_LEVEL, MAX_ICE_LEVEL);
        }
#endif
    }
}
