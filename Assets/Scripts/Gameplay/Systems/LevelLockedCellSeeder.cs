using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;
using Random = System.Random;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Puts the locked cells a level authors (<see cref="LevelObjectiveConfig.LockedCells"/>) onto the
    /// board at the opening of a run (issue #434). Mirrors <see cref="LevelReinforcedCellSeeder"/> in
    /// every way: same reason for being called inline from <see cref="BoardSystem.StartNewRun"/> rather
    /// than off <c>RunStartedMessage</c> (a lock brings its own block and must be standing before the
    /// player's first placement, and the ordering must be a property of one call stack), same reason
    /// for being its own class rather than code inside <see cref="BoardSystem"/>, same "which level"
    /// read, and the same <see cref="WeightedPieceDraw"/> for the block's cosmetic colour.
    /// <para>
    /// It also rolls each lock's visual skin (AC9 — one of <see cref="Board.LOCKED_SKIN_COUNT"/> approved
    /// looks, assigned at random per instance so a level shows a visibly different lock from one cell to
    /// the next); since issue #438 the reinforced seeder rolls from the same three looks the same way, the
    /// two mechanics sharing one set of art. The roll comes from this seeder's own
    /// <see cref="Random"/>, seeded from the clock in play and from a fixed value in tests, exactly the
    /// pattern <see cref="WeightedPieceDraw"/> and <see cref="BoardSystem"/> keep for their own draws;
    /// <c>UnityEngine.Random</c> is deliberately not used, so a test can pin the outcome.
    /// </para>
    /// </summary>
    public sealed class LevelLockedCellSeeder
    {
        private readonly LevelCatalog _levelCatalog;
        private readonly LevelProgressionModel _progressionModel;
        private readonly PathRunModel _pathRunModel;
        private readonly WeightedPieceDraw _pieceDraw;
        private readonly Random _random;

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public LevelLockedCellSeeder(
            LevelCatalog levelCatalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            WeightedPieceDraw pieceDraw)
            : this(levelCatalog, progressionModel, pathRunModel, pieceDraw, Environment.TickCount)
        {
        }

        internal LevelLockedCellSeeder(
            LevelCatalog levelCatalog,
            LevelProgressionModel progressionModel,
            PathRunModel pathRunModel,
            WeightedPieceDraw pieceDraw,
            int seed)
        {
            _levelCatalog = levelCatalog;
            _progressionModel = progressionModel;
            _pathRunModel = pathRunModel;
            _pieceDraw = pieceDraw;
            _random = new Random(seed);
        }

        /// <summary>
        /// Occupies the current level's authored locked cells on <paramref name="boardModel"/>, which
        /// the caller has just emptied, each with its authored threshold and a freshly rolled skin.
        /// <para>
        /// An authored cell that cannot be occupied — off the board, a hole, or already taken by an
        /// earlier seeder or an earlier entry that named the same position — is skipped rather than
        /// fatal, exactly as the reinforced seeder skips its own. <see cref="LevelObjectiveConfig.IsValid"/>
        /// rejects all of these at authoring time (and a threshold above the position's real neighbour
        /// count, AC5), so this is a defensive path and not a normal one, and a level with one bad entry
        /// still opens.
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

            IReadOnlyList<LockedCellAuthoring> authored = level.LockedCells;
            for (int i = 0; i < authored.Count; i++)
            {
                LockedCellAuthoring entry = authored[i];
                if (entry == null)
                {
                    continue;
                }

                GridPosition position = entry.ToGridPosition();
                if (!boardModel.IsPlayable(position) || boardModel.GetCell(position) != Board.EMPTY)
                {
                    Debug.LogWarning(
                        $"Level {level.LevelNumber} authors a locked cell at {position}, which is "
                        + "not an empty playable cell on this board. Skipped.");
                    continue;
                }

                // The block's colour comes from the same draw ordinary dock pieces take theirs from —
                // cosmetic, and mostly hidden under the skin overlay anyway. The skin is the seeder's
                // own roll: nothing about it is authored (AC9).
                int skin = _random.Next(0, Board.LOCKED_SKIN_COUNT);
                boardModel.OccupyLocked(position, _pieceDraw.DrawColourId(), entry.UnlockThreshold, skin);
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
