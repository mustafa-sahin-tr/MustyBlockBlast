using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Puts the reinforced cells a level authors
    /// (<see cref="LevelObjectiveConfig.ReinforcedCells"/>) onto the board at the opening of a run.
    /// <para>
    /// <b>Why this is called directly rather than subscribing to <c>RunStartedMessage</c>, unlike
    /// <see cref="LevelCoinCellSeedSystem"/>.</b> That System waits for a later event because a coin
    /// cell is a <em>property of a block</em> — it is painted onto a cell that already holds one, and at
    /// run-start time the board is empty, so there is nothing to paint. A reinforced cell has the
    /// opposite shape: it brings its own block, occupying a previously empty cell (issue #153 AC1,
    /// "pre-filled and occupied from board creation"). So it has nothing to wait for, and waiting would
    /// be actively wrong — the player's first placement, and the first line it could complete, must
    /// already see the reinforced cells. <see cref="BoardSystem.StartNewRun"/> therefore calls
    /// <see cref="Seed"/> inline between clearing the board and dealing the tray, which makes the
    /// ordering a property of one call stack rather than of subscription order.
    /// </para>
    /// <para>
    /// Its own class rather than code inside <see cref="BoardSystem"/> so that System keeps knowing
    /// nothing about level authoring: it gains one collaborator instead of three
    /// (<see cref="LevelCatalog"/>, <see cref="LevelProgressionModel"/>, <see cref="PathRunModel"/>),
    /// and "which level is being played" stays with the classes that already answer it.
    /// </para>
    /// <para>
    /// Which level is read the same way the rest of the game reads it: the Path run's active level when
    /// there is one, otherwise the linear frontier. A level the catalog does not author, or one
    /// authoring no reinforced cells, seeds nothing.
    /// </para>
    /// </summary>
    public sealed class LevelReinforcedCellSeeder
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly WeightedPieceDraw _pieceDraw;

        [Inject]
        public LevelReinforcedCellSeeder(
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

        /// <summary>
        /// Occupies the current level's authored reinforced cells on <paramref name="boardModel"/>,
        /// which the caller has just emptied.
        /// <para>
        /// An authored cell that cannot be occupied — off the board, a hole, or already taken by an
        /// earlier entry that named the same position twice — is skipped rather than fatal.
        /// <see cref="LevelObjectiveConfig.IsValid"/> rejects all three at authoring time, so this is a
        /// defensive path and not a normal one, and a level with one bad entry still opens.
        /// </para>
        /// </summary>
        internal void Seed(BoardModel boardModel)
        {
            LevelObjectiveConfig level = _levelCatalog != null
                ? _levelCatalog.Find(CurrentLevelNumber())
                : null;
            if (level == null)
            {
                return;
            }

            IReadOnlyList<ReinforcedCellAuthoring> authored = level.ReinforcedCells;
            for (int i = 0; i < authored.Count; i++)
            {
                ReinforcedCellAuthoring entry = authored[i];
                if (entry == null)
                {
                    continue;
                }

                GridPosition position = entry.ToGridPosition();
                if (!boardModel.IsPlayable(position) || boardModel.GetCell(position) != Board.EMPTY)
                {
                    Debug.LogWarning(
                        $"Level {level.LevelNumber} authors a reinforced cell at {position}, which is "
                        + "not an empty playable cell on this board. Skipped.");
                    continue;
                }

                // The colour comes from the same draw ordinary dock pieces take theirs from, rather
                // than a bespoke id no theme defines: colour is cosmetic everywhere in this game, and
                // the damage tint is what marks the cell as reinforced.
                boardModel.OccupyReinforced(position, _pieceDraw.DrawColourId(), entry.HitCount);
            }
        }

        /// <summary>The level the run being opened is playing. The same distinction
        /// <see cref="LevelCoinCellSeedSystem"/> and <see cref="LevelProgressionSystem"/> keep, read
        /// rather than duplicated as state here.</summary>
        private int CurrentLevelNumber()
        {
            int activeLevel = _pathRunModel.ActiveLevelNumber.Value;
            return activeLevel != PathRunModel.NO_ACTIVE_LEVEL
                ? activeLevel
                : _progressionModel.CurrentLevelNumber.Value;
        }
    }
}
