using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One <see cref="SpecialCellKind.PowerStar"/> a level pre-fills its board with (issue #482): a
    /// position only — every star starts at charge 0 and bursts at
    /// <see cref="Board.POWER_STAR_BURST_CHARGE"/>. Seeded by <c>LevelPowerStarCellSeeder</c>.
    /// </summary>
    [Serializable]
    public sealed class PowerStarCellAuthoring
    {
        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        internal GridPosition ToGridPosition() => new GridPosition(_x, _y);
    }
}
