using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// One puzzle-link group a level pre-fills its board with (issue #483): 2-3 cells, each orthogonally
    /// next to another member, locked together. <c>LevelObjectiveConfig.IsValid</c> checks the size, the
    /// connectivity and that every cell is a playable one; <c>LevelPuzzleLinkSeeder</c> places it.
    /// </summary>
    [Serializable]
    public sealed class PuzzleLinkGroupAuthoring
    {
        public const int MIN_GROUP_SIZE = 2;
        public const int MAX_GROUP_SIZE = 3;

        [Tooltip("The group's cells as (column, row), 0-based from the bottom-left. 2-3 cells, each "
            + "orthogonally next to another member.")]
        [SerializeField] private List<Vector2Int> _cells = new List<Vector2Int>();

        /// <summary>The group's cells in authored order. Never null.</summary>
        public int CellCount => _cells != null ? _cells.Count : 0;

        internal GridPosition CellAt(int index) => new GridPosition(_cells[index].x, _cells[index].y);

        /// <summary>Whether every member is orthogonally reachable from the first through other members —
        /// a flood over the group's own (at most three) cells.</summary>
        internal bool IsConnected()
        {
            int count = CellCount;
            if (count == 0)
            {
                return false;
            }

            var reached = new bool[count];
            reached[0] = true;
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int from = 0; from < count; from++)
                {
                    if (!reached[from])
                    {
                        continue;
                    }

                    for (int to = 0; to < count; to++)
                    {
                        if (!reached[to] && AreNeighbours(_cells[from], _cells[to]))
                        {
                            reached[to] = true;
                            grew = true;
                        }
                    }
                }
            }

            for (int index = 0; index < count; index++)
            {
                if (!reached[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreNeighbours(Vector2Int a, Vector2Int b)
            => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
    }
}
