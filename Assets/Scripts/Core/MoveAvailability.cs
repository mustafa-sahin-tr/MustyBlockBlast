using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>Detects whether any tray piece still fits on the board. Cheap enough to run after
    /// every placement and every tray refill (see docs/game-design.md, "Game over").</summary>
    public static class MoveAvailability
    {
        /// <summary>True if there is at least one board position where the piece could be placed.</summary>
        public static bool CanPlaceAnywhere(Board board, Piece piece)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            for (int y = 0; y < Board.SIZE; y++)
            {
                for (int x = 0; x < Board.SIZE; x++)
                {
                    if (PlacementRules.CanPlace(board, piece, new GridPosition(x, y)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>True if any piece in the tray still fits somewhere on the board. False (no
        /// moves left) means the run is over.</summary>
        public static bool HasAnyMove(Board board, IReadOnlyList<Piece> trayPieces)
        {
            if (trayPieces == null)
            {
                throw new ArgumentNullException(nameof(trayPieces));
            }

            for (int i = 0; i < trayPieces.Count; i++)
            {
                if (CanPlaceAnywhere(board, trayPieces[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
