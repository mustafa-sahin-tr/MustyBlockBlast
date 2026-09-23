using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Marks the ice sockets a level authors (<see cref="LevelObjectiveConfig.TargetIceCells"/>) on
    /// the board at the opening of a run (issue #433). Mirrors <see cref="LevelReinforcedCellSeeder"/>
    /// in every way but one: same reason for being called inline from
    /// <see cref="BoardSystem.StartNewRun"/> rather than off <c>RunStartedMessage</c> (the marker has to
    /// be on the board before the player's first placement, and the ordering must be a property of one
    /// call stack), same reason for being its own class rather than code inside
    /// <see cref="BoardSystem"/>, and same "which level" read.
    /// <para>
    /// The one structural divergence: this seeder does NOT occupy anything. An ice socket starts a run
    /// empty and playable (AC2), so it writes only the ice marker
    /// (<see cref="BoardModel.SetIceLevel"/>) and leaves the cell itself <see cref="Board.EMPTY"/> —
    /// which is also why, unlike the reinforced and timer seeders, it takes no
    /// <see cref="WeightedPieceDraw"/>: there is no block to colour.
    /// </para>
    /// </summary>
    public sealed class LevelTargetIceCellSeeder
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;

        [Inject]
        public LevelTargetIceCellSeeder(
            LevelCatalog levelCatalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel)
        {
            _levelCatalog = levelCatalog;
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
        }

        /// <summary>
        /// Marks the current level's authored ice sockets on <paramref name="boardModel"/>, which the
        /// caller has just emptied (ice included — see <see cref="BoardModel.ClearAll"/>).
        /// <para>
        /// An authored socket that cannot be marked — off the board, a hole, or a cell an earlier seeder
        /// already pre-filled with a reinforced or timer block — is skipped rather than fatal.
        /// <see cref="LevelObjectiveConfig.IsValid"/> rejects all of these at authoring time, so this is
        /// a defensive path and not a normal one, and a level with one bad entry still opens. A position
        /// this seeder already marked is simply overwritten by the later entry.
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

            IReadOnlyList<TargetIceCellAuthoring> authored = level.TargetIceCells;
            for (int i = 0; i < authored.Count; i++)
            {
                TargetIceCellAuthoring entry = authored[i];
                if (entry == null)
                {
                    continue;
                }

                GridPosition position = entry.ToGridPosition();
                if (!boardModel.IsPlayable(position) || boardModel.GetCell(position) != Board.EMPTY)
                {
                    Debug.LogWarning(
                        $"Level {level.LevelNumber} authors an ice socket at {position}, which is "
                        + "not an empty playable cell on this board. Skipped.");
                    continue;
                }

                // The marker only — the cell stays empty. Nothing is drawn from the piece palette,
                // because there is no block here to colour until the player places one.
                boardModel.SetIceLevel(position, entry.IceLevel);
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
