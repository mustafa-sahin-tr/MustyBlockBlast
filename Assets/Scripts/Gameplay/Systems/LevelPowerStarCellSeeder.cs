using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Pre-fills the current level's authored power stars (issue #482) at run start, exactly as
    /// <see cref="LevelTimerCellSeeder"/> does its timer cells: each at charge 0, in a colour from the dock's
    /// own draw. An entry on a cell that is not an empty playable cell is skipped with a warning.
    /// </summary>
    public sealed class LevelPowerStarCellSeeder
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly WeightedPieceDraw _pieceDraw;

        [Inject]
        public LevelPowerStarCellSeeder(
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

            IReadOnlyList<PowerStarCellAuthoring> authored = level.PowerStarCells;
            for (int i = 0; i < authored.Count; i++)
            {
                PowerStarCellAuthoring entry = authored[i];
                if (entry == null)
                {
                    continue;
                }

                GridPosition position = entry.ToGridPosition();
                if (!boardModel.IsPlayable(position) || boardModel.GetCell(position) != Board.EMPTY)
                {
                    Debug.LogWarning(
                        $"Level {level.LevelNumber} authors a power star at {position}, which is "
                        + "not an empty playable cell on this board. Skipped.");
                    continue;
                }

                boardModel.OccupyPowerStar(position, _pieceDraw.DrawColourId());
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
