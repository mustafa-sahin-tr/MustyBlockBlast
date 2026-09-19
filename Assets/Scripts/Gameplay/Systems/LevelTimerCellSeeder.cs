using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Puts the timer cells a level authors (<see cref="LevelObjectiveConfig.TimerCells"/>) onto the
    /// board at the opening of a run. Mirrors <see cref="LevelReinforcedCellSeeder"/> in every way that
    /// matters (issue #307 AC7/AC8): same reason for being called inline from
    /// <see cref="BoardSystem.StartNewRun"/> rather than off <c>RunStartedMessage</c> (a timer cell
    /// brings its own block, occupying a previously empty cell, so it has nothing to wait for and must
    /// already be standing before the player's first placement), same reason for being its own class
    /// rather than code inside <see cref="BoardSystem"/>, and same "which level" read.
    /// </summary>
    public sealed class LevelTimerCellSeeder
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly WeightedPieceDraw _pieceDraw;

        [Inject]
        public LevelTimerCellSeeder(
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
        /// Occupies the current level's authored timer cells on <paramref name="boardModel"/>, which the
        /// caller has just emptied (and, when both mechanics are seeded on the same run, has typically
        /// already seeded its reinforced cells onto — see <see cref="BoardSystem.StartNewRun"/> for the
        /// order).
        /// <para>
        /// An authored cell that cannot be occupied — off the board, a hole, or already taken by an
        /// earlier entry (this seeder's own, or a reinforced cell the run already placed) — is skipped
        /// rather than fatal. <see cref="LevelObjectiveConfig.IsValid"/> rejects all of these at
        /// authoring time, so this is a defensive path and not a normal one.
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

            IReadOnlyList<TimerCellAuthoring> authored = level.TimerCells;
            for (int i = 0; i < authored.Count; i++)
            {
                TimerCellAuthoring entry = authored[i];
                if (entry == null)
                {
                    continue;
                }

                GridPosition position = entry.ToGridPosition();
                if (!boardModel.IsPlayable(position) || boardModel.GetCell(position) != Board.EMPTY)
                {
                    Debug.LogWarning(
                        $"Level {level.LevelNumber} authors a timer cell at {position}, which is "
                        + "not an empty playable cell on this board. Skipped.");
                    continue;
                }

                // The colour comes from the same draw ordinary dock pieces take theirs from, exactly as
                // LevelReinforcedCellSeeder's own entries do: colour is cosmetic everywhere in this game.
                boardModel.OccupyTimer(position, _pieceDraw.DrawColourId(), entry.StartingCountdown);
            }
        }

        /// <summary>The level the run being opened is playing — the same distinction
        /// <see cref="LevelReinforcedCellSeeder"/> reads rather than duplicates as state here.</summary>
        private int CurrentLevelNumber()
        {
            int activeLevel = _pathRunModel.ActiveLevelNumber.Value;
            return activeLevel != PathRunModel.NO_ACTIVE_LEVEL
                ? activeLevel
                : _progressionModel.CurrentLevelNumber.Value;
        }
    }
}
