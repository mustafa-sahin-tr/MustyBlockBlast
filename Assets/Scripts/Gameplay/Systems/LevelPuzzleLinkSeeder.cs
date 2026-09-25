using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Pre-fills the current level's authored puzzle-link groups (issue #483) at run start: every cell of
    /// group N (1-based, in authored order) becomes a <see cref="SpecialCellKind.PuzzleLink"/> of group N,
    /// the whole group in one colour from the dock's draw so it reads as one piece. A group with any cell
    /// that is not an empty playable one is skipped whole, with a warning — half a group would be a group
    /// the player could never complete.
    /// </summary>
    public sealed class LevelPuzzleLinkSeeder
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly WeightedPieceDraw _pieceDraw;

        [Inject]
        public LevelPuzzleLinkSeeder(
            LevelCatalog levelCatalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            WeightedPieceDraw pieceDraw)
        {
            _levelCatalog = levelCatalog;
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
            _pieceDraw = pieceDraw;
        }

        internal void Seed(BoardModel boardModel)
        {
            LevelObjectiveConfig level = _levelCatalog != null
                ? _levelCatalog.Find(CurrentLevelNumber())
                : null;
            if (level == null)
            {
                return;
            }

            IReadOnlyList<PuzzleLinkGroupAuthoring> groups = level.PuzzleLinkGroups;
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                PuzzleLinkGroupAuthoring group = groups[groupIndex];
                if (group == null || group.CellCount == 0)
                {
                    continue;
                }

                bool placeable = true;
                for (int cellIndex = 0; cellIndex < group.CellCount && placeable; cellIndex++)
                {
                    GridPosition position = group.CellAt(cellIndex);
                    placeable = boardModel.IsPlayable(position) && boardModel.GetCell(position) == Board.EMPTY;
                }

                if (!placeable)
                {
                    Debug.LogWarning(
                        $"Level {level.LevelNumber} authors puzzle-link group {groupIndex + 1} over a cell that "
                        + "is not an empty playable cell on this board. The whole group is skipped.");
                    continue;
                }

                int colourId = _pieceDraw.DrawColourId();
                for (int cellIndex = 0; cellIndex < group.CellCount; cellIndex++)
                {
                    boardModel.OccupyPuzzleLink(group.CellAt(cellIndex), colourId, groupIndex + 1);
                }
            }
        }

        private int CurrentLevelNumber()
        {
            int activeLevel = _pathRunModel.ActiveLevelNumber.Value;
            return activeLevel != PathRunModel.NO_ACTIVE_LEVEL
                ? activeLevel
                : _progressionModel.CurrentLevelNumber.Value;
        }
    }
}
