using System;

namespace MustyBlockBlast.Core
{
    /// <summary>Whether and how a piece may be placed on the board. No gravity, no rotation.</summary>
    public static class PlacementRules
    {
        /// <summary>True when every cell the piece would occupy, anchored at <paramref name="anchor"/>,
        /// is part of the board's playable shape and currently empty. A hole is refused exactly as an
        /// off-board cell is: neither is somewhere a block can ever stand.</summary>
        public static bool CanPlace(Board board, Piece piece, GridPosition anchor)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                GridPosition cell = anchor + piece.Offsets[i];
                if (!board.IsPlayable(cell) || board.IsOccupied(cell))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Occupies every cell of the piece with <paramref name="colourId"/>. Throws if the
        /// placement is not legal — callers must check <see cref="CanPlace"/> first.</summary>
        public static void Place(Board board, Piece piece, GridPosition anchor, int colourId)
        {
            if (!CanPlace(board, piece, anchor))
            {
                throw new InvalidOperationException($"Piece '{piece}' cannot be placed at {anchor}.");
            }

            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                board.Occupy(anchor + piece.Offsets[i], colourId);
            }
        }
    }
}
