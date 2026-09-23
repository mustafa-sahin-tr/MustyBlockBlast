using System.Collections.Generic;
using MessagePipe;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Messages;
using MustyBlockBlast.Gameplay.Models;
using MustyBlockBlast.Gameplay.Settings;
using UnityEngine;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Development-only test scenarios, never compiled into a release build. Today it only sets up
    /// the one scenario manual testing keeps needing by hand: a board one placement away from
    /// clearing a row and a column at once, which is what spawns a <see cref="SpecialCellKind.ExplosiveCore"/>
    /// (see <see cref="ExplosiveCoreSpawnSelector"/>). Wholly independent of level content — it wipes
    /// and refills the current board rather than reading any level data — so it works the same on
    /// every level and every board shape.
    /// </summary>
    public sealed class DebugCheatSystem
    {
        /// <summary>Any valid colour id. The scenario does not care which — only that the row and
        /// column it fills are occupied.</summary>
        private const int TEST_COLOUR_ID = 1;

        /// <summary>The dock slot the matching 1x1 is dropped into. Slot 0 rather than a search for an
        /// empty one: this scenario is meant to be triggered on demand, and overwriting whatever slot 0
        /// currently holds is the point.</summary>
        private const int TARGET_SLOT_INDEX = 0;

        /// <summary>Id stamped on every debug-cycled objective. Distinct from any authored
        /// <c>level_N</c> id so a progress/completion message for it can never be mistaken for a real
        /// level's, and stable across cycles so the HUD never treats one debug objective as a different
        /// one mid-session.</summary>
        private const string DEBUG_OBJECTIVE_ID = "debug_cycle_objective";

        /// <summary>How many reinforced cells <see cref="CycleObjective"/> seeds onto the board when it
        /// lands on <see cref="ObjectiveType.ReinforcedCellsCleared"/> — that objective cannot progress
        /// without real reinforced cells to clear, unlike every other type this cycles through, which
        /// reads existing run state instead of needing anything spawned for it.</summary>
        private const int DEBUG_REINFORCED_CELL_COUNT = 2;

        /// <summary>Hit count each debug-seeded reinforced cell gets — the top of the shipped
        /// <c>MIN_HIT_COUNT..MAX_HIT_COUNT</c> range (see <see cref="ReinforcedCellAuthoring"/>), since a
        /// designer testing "how many hits does this survive" wants the hardest case by default.</summary>
        private const int DEBUG_REINFORCED_HIT_COUNT = ReinforcedCellAuthoring.MAX_HIT_COUNT;

        /// <summary>How many ice sockets <see cref="CycleObjective"/> marks when it lands on
        /// <see cref="ObjectiveType.IceCellsCleared"/> (issue #433), for the reason
        /// <see cref="DEBUG_REINFORCED_CELL_COUNT"/> exists: that objective cannot progress without real
        /// sockets to melt.</summary>
        private const int DEBUG_ICE_CELL_COUNT = 2;

        /// <summary>Ice level each debug-marked socket gets — the top of the shipped range (see
        /// <see cref="TargetIceCellAuthoring"/>), the hardest case by default.</summary>
        private const int DEBUG_ICE_LEVEL = 3;

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;
        private readonly ObjectiveModel _objectiveModel;
        private readonly IPublisher<ObjectiveProgressChangedMessage> _objectiveProgressChangedPublisher;

        private ObjectiveType _debugObjectiveType = ObjectiveType.SimultaneousLineClear;
        private bool _hasDebugObjectiveType;

        [Inject]
        public DebugCheatSystem(
            BoardModel boardModel,
            TrayModel trayModel,
            ObjectiveModel objectiveModel,
            IPublisher<ObjectiveProgressChangedMessage> objectiveProgressChangedPublisher)
        {
            _boardModel = boardModel;
            _trayModel = trayModel;
            _objectiveModel = objectiveModel;
            _objectiveProgressChangedPublisher = objectiveProgressChangedPublisher;
        }

        /// <summary>
        /// Clears the board, fills every playable cell of one row and one column except their
        /// intersection, and drops a single 1x1 into the dock's first slot. Dragging that piece onto
        /// the empty intersection completes both lines in the one placement that spawns an
        /// <see cref="SpecialCellKind.ExplosiveCore"/>.
        /// <para>
        /// The row and column are chosen as close to the board's centre as the current shape allows,
        /// so the scenario reads clearly even on a shape with holes near the edges. Does nothing but
        /// log a warning if the shape has no playable cell at all.
        /// </para>
        /// </summary>
        public void SetupCrossClearScenario()
        {
            GridPosition? target = FindCentreMostPlayableCell();
            if (target == null)
            {
                Debug.LogWarning(
                    $"{nameof(DebugCheatSystem)}: board shape has no playable cell, nothing to set up.");
                return;
            }

            _boardModel.ClearAll();

            GridPosition intersection = target.Value;
            FillRowExceptColumn(intersection.Y, intersection.X);
            FillColumnExceptRow(intersection.X, intersection.Y);

            _trayModel.SetSlot(TARGET_SLOT_INDEX, PieceCatalog.SingleCell, TEST_COLOUR_ID);
        }

        /// <summary>
        /// Paints or erases one board cell for the freeform paint tool: an already-occupied cell is
        /// emptied, an empty one is occupied with <paramref name="colourId"/> — a stroke that crosses a
        /// filled cell clears it exactly as one crossing an empty cell fills it, so painting and erasing
        /// are the one gesture. Silently does nothing on a hole or off-board position — a stroke that
        /// drags across a hole should not throw, it should just skip it exactly as dragging a piece over
        /// one would refuse it.
        /// </summary>
        public void ToggleCell(GridPosition position, int colourId)
        {
            if (!_boardModel.IsPlayable(position))
            {
                return;
            }

            if (_boardModel.GetCell(position) != Board.EMPTY)
            {
                _boardModel.Clear(position);
                return;
            }

            _boardModel.Occupy(position, colourId);
        }

        /// <summary>
        /// Steps one dock slot to the next (or previous) entry of <see cref="PieceCatalog.AllPieces"/>,
        /// wrapping around, and logs the shape it landed on — the paint tool's answer to "which shapes
        /// exist and which one is this slot showing now", since there is no on-screen picker for it.
        /// An empty slot starts the cycle at the catalog's first entry.
        /// </summary>
        public void CycleTraySlotPiece(int slotIndex, int colourId, bool forward)
        {
            IReadOnlyList<Piece> allPieces = PieceCatalog.AllPieces;
            if (allPieces.Count == 0)
            {
                return;
            }

            int currentIndex = IndexOf(allPieces, _trayModel.GetPiece(slotIndex));
            int step = forward ? 1 : -1;
            int nextIndex = ((currentIndex + step) % allPieces.Count + allPieces.Count) % allPieces.Count;

            Piece nextPiece = allPieces[nextIndex];
            _trayModel.SetSlot(slotIndex, nextPiece, colourId);
            Debug.Log(
                $"{nameof(DebugCheatSystem)}: slot {slotIndex} -> '{nextPiece.Id}' "
                + $"({nextIndex + 1}/{allPieces.Count})");
        }

        /// <summary>
        /// Steps the currently tracked objective to the next (or previous) <see cref="ObjectiveType"/>,
        /// wrapping around, and replaces <see cref="ObjectiveModel"/>'s tracked set with a single
        /// freshly-built objective of that type — the paint tool's answer to "let me try every level
        /// objective by hand" the same way <see cref="CycleTraySlotPiece"/> answers it for tray shapes.
        /// <para>
        /// Every type is built with the same level-agnostic defaults <see cref="LevelObjectiveConfig"/>
        /// ships as its own field defaults (2 lines, the Single family, 52 occupancy, "square_3x3", a
        /// 15s window, colour id 1), so a type this cycles into is always a valid, completable objective
        /// regardless of what level or run state it lands on. <see cref="ObjectiveType.ReinforcedCellsCleared"/>
        /// is the one exception: it cannot progress without real reinforced cells to clear, so landing on
        /// it also seeds <see cref="DEBUG_REINFORCED_CELL_COUNT"/> of them onto random empty cells.
        /// </para>
        /// </summary>
        public void CycleObjective(bool forward)
        {
            int typeCount = System.Enum.GetValues(typeof(ObjectiveType)).Length;
            int currentIndex = (int)(_hasDebugObjectiveType
                ? _debugObjectiveType
                : _objectiveModel.CurrentObjective?.Definition.Type ?? ObjectiveType.SimultaneousLineClear);

            int step = forward ? 1 : -1;
            int nextIndex = ((currentIndex + step) % typeCount + typeCount) % typeCount;
            var nextType = (ObjectiveType)nextIndex;

            ObjectiveDefinition definition = BuildDebugObjectiveDefinition(nextType);
            var progress = new ObjectiveProgress(definition);
            _objectiveModel.SetCurrentObjective(progress);

            _debugObjectiveType = nextType;
            _hasDebugObjectiveType = true;

            // ObjectiveIconContainerView only repaints on RunStarted/ObjectiveProgressChanged/
            // ObjectiveCompleted — SetCurrentObjective alone leaves the HUD showing whatever it drew
            // last, so this stands in for the "something changed" nudge a real placement would publish.
            _objectiveProgressChangedPublisher.Publish(new ObjectiveProgressChangedMessage(
                definition.Id, progress.CurrentValue, definition.TargetValue));

            Debug.Log($"{nameof(DebugCheatSystem)}: objective -> {nextType} (target {definition.TargetValue})");
        }

        private ObjectiveDefinition BuildDebugObjectiveDefinition(ObjectiveType type)
        {
            const int DEFAULT_TARGET = 3;
            const int SCORE_TARGET = 500;

            int targetValue = type switch
            {
                ObjectiveType.ScoreInRun => SCORE_TARGET,
                ObjectiveType.EarlyScoreRush => SCORE_TARGET,
                ObjectiveType.ReinforcedCellsCleared => SeedDebugReinforcedCells(),
                ObjectiveType.IceCellsCleared => SeedDebugIceCells(),
                _ => DEFAULT_TARGET,
            };

            return new ObjectiveDefinition(
                DEBUG_OBJECTIVE_ID,
                type,
                ObjectiveScope.PerRun,
                targetValue,
                requiredLineCount: 2,
                requiredPieceFamily: PieceFamily.Single,
                requiredOccupancyThreshold: 52,
                requiredPieceId: "square_3x3",
                windowSeconds: 15f,
                requiredColourId: 1);
        }

        /// <summary>
        /// Occupies up to <see cref="DEBUG_REINFORCED_CELL_COUNT"/> random empty, playable cells as
        /// reinforced cells with <see cref="DEBUG_REINFORCED_HIT_COUNT"/> hits each, through the exact
        /// same <see cref="BoardModel.OccupyReinforced"/> path level-start seeding uses — so the board,
        /// visuals and hit-count bookkeeping behave identically to a real authored level. Returns the
        /// number actually seeded, clamped to at least 1 so the objective's target is never zero (which
        /// <see cref="ObjectiveDefinition"/> would reject) even on a board with no empty cell left.
        /// </summary>
        private int SeedDebugReinforcedCells()
        {
            var candidates = new List<GridPosition>();
            for (int y = 0; y < _boardModel.Height; y++)
            {
                for (int x = 0; x < _boardModel.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.IsPlayable(position) && _boardModel.GetCell(position) == Board.EMPTY)
                    {
                        candidates.Add(position);
                    }
                }
            }

            int seededCount = Mathf.Min(DEBUG_REINFORCED_CELL_COUNT, candidates.Count);
            for (int i = 0; i < seededCount; i++)
            {
                int pick = Random.Range(0, candidates.Count);

                // Skin rolled the way the level seeder rolls it (issue #438), from the same draw this
                // cheat already uses for the position: a designer testing the look wants all three.
                int skin = Random.Range(0, Board.LOCKED_SKIN_COUNT);
                _boardModel.OccupyReinforced(candidates[pick], TEST_COLOUR_ID, DEBUG_REINFORCED_HIT_COUNT, skin);
                candidates.RemoveAt(pick);
            }

            if (seededCount == 0)
            {
                Debug.LogWarning($"{nameof(DebugCheatSystem)}: no empty playable cell to seed a reinforced "
                    + "cell on — objective set with an unreachable target of 1.");
            }

            return Mathf.Max(seededCount, 1);
        }

        /// <summary>The ice-socket counterpart of <see cref="SeedDebugReinforcedCells"/> (issue #433):
        /// marks up to <see cref="DEBUG_ICE_CELL_COUNT"/> random empty playable cells with
        /// <see cref="DEBUG_ICE_LEVEL"/> levels of ice, leaving them empty, and returns how many it
        /// marked so the debug objective's target is "all of them" exactly as a level's would be.</summary>
        private int SeedDebugIceCells()
        {
            var candidates = new List<GridPosition>();
            for (int y = 0; y < _boardModel.Height; y++)
            {
                for (int x = 0; x < _boardModel.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (_boardModel.IsPlayable(position) && _boardModel.GetCell(position) == Board.EMPTY
                        && _boardModel.GetIceLevel(position) == 0)
                    {
                        candidates.Add(position);
                    }
                }
            }

            int markedCount = Mathf.Min(DEBUG_ICE_CELL_COUNT, candidates.Count);
            for (int i = 0; i < markedCount; i++)
            {
                int pick = Random.Range(0, candidates.Count);
                _boardModel.SetIceLevel(candidates[pick], DEBUG_ICE_LEVEL);
                candidates.RemoveAt(pick);
            }

            if (markedCount == 0)
            {
                Debug.LogWarning($"{nameof(DebugCheatSystem)}: no empty playable cell to mark an ice "
                    + "socket on — objective set with an unreachable target of 1.");
            }

            return Mathf.Max(markedCount, 1);
        }

        private static int IndexOf(IReadOnlyList<Piece> pieces, Piece piece)
        {
            if (piece == null)
            {
                return -1;
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i].Id == piece.Id)
                {
                    return i;
                }
            }

            return -1;
        }

        private GridPosition? FindCentreMostPlayableCell()
        {
            int centreX = _boardModel.Width / 2;
            int centreY = _boardModel.Height / 2;

            GridPosition? best = null;
            int bestDistance = int.MaxValue;

            for (int y = 0; y < _boardModel.Height; y++)
            {
                for (int x = 0; x < _boardModel.Width; x++)
                {
                    var position = new GridPosition(x, y);
                    if (!_boardModel.IsPlayable(position))
                    {
                        continue;
                    }

                    int distance = Mathf.Abs(x - centreX) + Mathf.Abs(y - centreY);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = position;
                    }
                }
            }

            return best;
        }

        private void FillRowExceptColumn(int y, int excludedX)
        {
            for (int x = 0; x < _boardModel.Width; x++)
            {
                if (x == excludedX)
                {
                    continue;
                }

                var position = new GridPosition(x, y);
                if (_boardModel.IsPlayable(position))
                {
                    _boardModel.Occupy(position, TEST_COLOUR_ID);
                }
            }
        }

        private void FillColumnExceptRow(int x, int excludedY)
        {
            for (int y = 0; y < _boardModel.Height; y++)
            {
                if (y == excludedY)
                {
                    continue;
                }

                var position = new GridPosition(x, y);
                if (_boardModel.IsPlayable(position))
                {
                    _boardModel.Occupy(position, TEST_COLOUR_ID);
                }
            }
        }
    }
#endif
}
