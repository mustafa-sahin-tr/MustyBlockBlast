using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;
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

        private readonly BoardModel _boardModel;
        private readonly TrayModel _trayModel;

        [Inject]
        public DebugCheatSystem(BoardModel boardModel, TrayModel trayModel)
        {
            _boardModel = boardModel;
            _trayModel = trayModel;
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
