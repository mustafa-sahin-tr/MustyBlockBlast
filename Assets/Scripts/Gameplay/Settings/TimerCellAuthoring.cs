using System;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One authored <see cref="SpecialCellKind.Timer"/> cell on a level's board: where it stands and how
    /// many placements its countdown starts at. Mirrors <see cref="ReinforcedCellAuthoring"/>'s shape
    /// deliberately (issue #307 AC7) — the two kinds of authored, pre-filled board furniture are edited
    /// the same way in the Inspector.
    /// <para>
    /// No colour is authored, for the same reason <see cref="ReinforcedCellAuthoring"/> authors none:
    /// colour is cosmetic everywhere in this game and never affects placement or clearing, so the seeder
    /// draws one from the same palette ordinary pieces come from.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class TimerCellAuthoring
    {
        /// <summary>Fewest placements a timer cell may be authored to start with (issue #307 AC7) —
        /// deliberately the same figure as <see cref="ReinforcedCellAuthoring.MIN_HIT_COUNT"/>.</summary>
        public const int MIN_STARTING_COUNTDOWN = 2;

        /// <summary>Most placements a timer cell may be authored to start with — deliberately the same
        /// figure as <see cref="ReinforcedCellAuthoring.MAX_HIT_COUNT"/>.</summary>
        public const int MAX_STARTING_COUNTDOWN = 4;

        [Tooltip("Column, 0-based from the left.")]
        [SerializeField] private int _x;

        [Tooltip("Row, 0-based from the bottom.")]
        [SerializeField] private int _y;

        [Tooltip("Placements this cell's countdown starts at before it converts to an ordinary cell. 2-4.")]
        [SerializeField] private int _startingCountdown = MIN_STARTING_COUNTDOWN;

        /// <summary>Placements this cell starts the level with before its countdown reaches 0.</summary>
        public int StartingCountdown => _startingCountdown;

        internal GridPosition ToGridPosition() => new GridPosition(_x, _y);

#if UNITY_EDITOR
        /// <summary>Clamps the authored countdown into range, called by
        /// <see cref="LevelObjectiveConfig.ValidateInEditor"/> exactly as that method clamps
        /// <see cref="ReinforcedCellAuthoring"/>'s own hit count.</summary>
        internal void ValidateInEditor()
        {
            _startingCountdown = Mathf.Clamp(_startingCountdown, MIN_STARTING_COUNTDOWN, MAX_STARTING_COUNTDOWN);
        }
#endif
    }
}
