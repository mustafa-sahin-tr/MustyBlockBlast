using System;
using MustyBlockBlast.Core;
using VContainer;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Weighted draw from <see cref="PieceCatalog"/>: large pieces are rarer than small ones
    /// (docs/game-design.md, "Drawing pieces"). Solvability is deliberately not guaranteed —
    /// <see cref="DrawPiece"/> is the ordinary refill draw and stays exactly that.
    /// <para>
    /// <see cref="TryDrawSolvableSet"/> is the one exception, and it is scoped to the Reroll power-up
    /// alone: a set the player paid for should not be dead on arrival. It is a separate method rather
    /// than a flag on the ordinary draw precisely so no refill path can acquire the guarantee by
    /// accident. <see cref="TryDrawLineClearingSet"/> is the same guarantee with a stronger preference
    /// layered on top, under the same rule: a separate method, never a flag on the ordinary draw.
    /// </para>
    /// </summary>
    public sealed class WeightedPieceDraw
    {
        /// <summary>Number of distinct cosmetic colour ids ("piece kinds"). Colour never affects rules.</summary>
        public const int COLOUR_COUNT = Board.COLOUR_COUNT;

        /// <summary>
        /// How many whole sets <see cref="TryDrawSolvableSet"/> will draw before giving up on the
        /// solvability guarantee. Generous but finite: the catalog is weighted heavily towards small
        /// pieces (a 1x1 or 1x2 is ten times as likely as a 3x3), so any board with a single empty cell
        /// resolves on the first attempt with high probability and the cap is never approached in
        /// practice. It exists to bound the pathological case, not the ordinary one.
        /// </summary>
        public const int MAX_SOLVABLE_DRAW_ATTEMPTS = 16;

        private readonly Random _random;
        private readonly int[] _weights;
        private readonly int _totalWeight;

        /// <summary>Owned evaluator for <see cref="TryDrawLineClearingSet"/>; it carries its own scratch
        /// board, so the draw's repeated per-piece checks allocate nothing.</summary>
        private readonly LineClearOpportunity _lineClearOpportunity = new LineClearOpportunity();

        /// <summary>Holds the best set <see cref="TryDrawLineClearingSet"/> has seen so far while later
        /// attempts overwrite the caller's buffers. Grown to the caller's slot count on demand and kept,
        /// so repeated draws of the same tray size reuse it.</summary>
        private Piece[] _bestPieces = Array.Empty<Piece>();
        private int[] _bestColourIds = Array.Empty<int>();

        /// <summary>DI entry point — VContainer must not pick the seeded constructor.</summary>
        [Inject]
        public WeightedPieceDraw()
            : this(Environment.TickCount)
        {
        }

        public WeightedPieceDraw(int seed)
        {
            _random = new Random(seed);
            _weights = new int[PieceCatalog.AllPieces.Count];

            int total = 0;
            for (int i = 0; i < PieceCatalog.AllPieces.Count; i++)
            {
                int weight = WeightFor(PieceCatalog.AllPieces[i].CellCount);
                _weights[i] = weight;
                total += weight;
            }

            _totalWeight = total;
        }

        public Piece DrawPiece()
        {
            int roll = _random.Next(_totalWeight);
            for (int i = 0; i < _weights.Length; i++)
            {
                roll -= _weights[i];
                if (roll < 0)
                {
                    return PieceCatalog.AllPieces[i];
                }
            }

            return PieceCatalog.AllPieces[PieceCatalog.AllPieces.Count - 1];
        }

        public int DrawColourId() => _random.Next(1, COLOUR_COUNT + 1);

        /// <summary>
        /// Draws a whole set of pieces and colours into the caller's buffers, retrying until at least
        /// one of them has a legal placement on <paramref name="board"/>. Both buffers are filled
        /// completely and must be the same length; nothing is allocated per call.
        /// <para>
        /// The retry unit is the <em>whole set</em>, not the individual slot. The guarantee is "at least
        /// one of these fits", which is a property of the set — re-drawing only some slots would mean
        /// deciding which piece was at fault, a question the guarantee never asks.
        /// </para>
        /// <para>
        /// <b>Fallback:</b> returns false when <see cref="MAX_SOLVABLE_DRAW_ATTEMPTS"/> sets have all
        /// come up unplaceable, and leaves the last attempt's draw in the buffers. This is reachable —
        /// a board with no gap any catalog piece fits (a single isolated empty cell has only the 1x1)
        /// can make every set unplaceable, so no bound could ever guarantee success there. Handing back
        /// the last set is the graceful outcome: the caller always gets a complete, valid set of pieces,
        /// so the tray is never left empty and nothing is thrown. A board that defeats the guarantee was
        /// a board with no moves left anyway, and the caller's own game-over check resolves it.
        /// </para>
        /// </summary>
        public bool TryDrawSolvableSet(Board board, Piece[] pieces, int[] colourIds)
        {
            ValidateSetBuffers(board, pieces, colourIds);

            for (int attempt = 0; attempt < MAX_SOLVABLE_DRAW_ATTEMPTS; attempt++)
            {
                DrawSetInto(pieces, colourIds);

                // Arrays implement IReadOnlyList<T>, so the buffer is checked in place — the solvability
                // test costs nothing beyond the board scan itself.
                if (MoveAvailability.HasAnyMove(board, pieces))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Like <see cref="TryDrawSolvableSet"/>, but prefers a set in which at least one piece could
        /// clear a line. Draws <see cref="MAX_SOLVABLE_DRAW_ATTEMPTS"/> whole sets and keeps the best of
        /// them, ranked first by "at least one piece fits at all" and then by the most simultaneous lines
        /// any one piece in the set could clear (per <see cref="LineClearOpportunity"/>). The winner is
        /// left in the buffers, which are filled completely and must be the same length.
        /// <para>
        /// The whole budget is always spent rather than stopping at the first clearing set: the point is
        /// to maximise lines among the attempts tried, and a fixed attempt count keeps the draw
        /// deterministic for a given seed and board, exactly as the solvable draw's bound is.
        /// </para>
        /// <para>
        /// <b>Fallback:</b> returns false, with <paramref name="maxLineCount"/> at 0, when no attempt
        /// produced a clearing piece. The buffers then hold the best merely-solvable set seen, or — on a
        /// board where every attempt was unplaceable — the last attempt's draw. That is never worse than
        /// what <see cref="TryDrawSolvableSet"/> guarantees over the same budget: a solvable set is
        /// preferred over an unplaceable one at every step, and a complete set is always handed back.
        /// </para>
        /// </summary>
        /// <param name="maxLineCount">The most simultaneous lines any single piece in the returned set
        /// could clear with its best placement; 0 when the return value is false.</param>
        public bool TryDrawLineClearingSet(
            Board board, Piece[] pieces, int[] colourIds, out int maxLineCount)
        {
            ValidateSetBuffers(board, pieces, colourIds);
            EnsureBestBuffers(pieces.Length);

            bool hasBest = false;
            bool bestSolvable = false;
            int bestLineCount = 0;

            for (int attempt = 0; attempt < MAX_SOLVABLE_DRAW_ATTEMPTS; attempt++)
            {
                DrawSetInto(pieces, colourIds);

                int lineCount = MaxLinesAnyPieceClears(board, pieces);

                // A positive line count already proves the set fits somewhere; the board scan is only
                // needed to separate "fits but cannot clear" from "does not fit at all".
                bool solvable = lineCount > 0 || MoveAvailability.HasAnyMove(board, pieces);

                if (hasBest && !IsBetterSet(solvable, lineCount, bestSolvable, bestLineCount))
                {
                    continue;
                }

                Array.Copy(pieces, _bestPieces, pieces.Length);
                Array.Copy(colourIds, _bestColourIds, colourIds.Length);
                hasBest = true;
                bestSolvable = solvable;
                bestLineCount = lineCount;
            }

            Array.Copy(_bestPieces, pieces, pieces.Length);
            Array.Copy(_bestColourIds, colourIds, colourIds.Length);

            maxLineCount = bestLineCount;
            return bestLineCount > 0;
        }

        private static void ValidateSetBuffers(Board board, Piece[] pieces, int[] colourIds)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (pieces == null)
            {
                throw new ArgumentNullException(nameof(pieces));
            }

            if (colourIds == null)
            {
                throw new ArgumentNullException(nameof(colourIds));
            }

            if (colourIds.Length != pieces.Length)
            {
                throw new ArgumentException(
                    "Piece and colour buffers must be the same length.", nameof(colourIds));
            }
        }

        /// <summary>Strictly better on purpose: a tie leaves the incumbent, so the earliest of equally
        /// good attempts wins and the outcome is a pure function of seed and board.</summary>
        private static bool IsBetterSet(
            bool solvable, int lineCount, bool bestSolvable, int bestLineCount)
        {
            if (solvable != bestSolvable)
            {
                return solvable;
            }

            return lineCount > bestLineCount;
        }

        private void DrawSetInto(Piece[] pieces, int[] colourIds)
        {
            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                pieces[slotIndex] = DrawPiece();
                colourIds[slotIndex] = DrawColourId();
            }
        }

        private int MaxLinesAnyPieceClears(Board board, Piece[] pieces)
        {
            int best = 0;
            for (int slotIndex = 0; slotIndex < pieces.Length; slotIndex++)
            {
                int lineCount = _lineClearOpportunity.MaxLinesAnyPlacementClears(board, pieces[slotIndex]);
                if (lineCount > best)
                {
                    best = lineCount;
                }
            }

            return best;
        }

        private void EnsureBestBuffers(int slotCount)
        {
            if (_bestPieces.Length < slotCount)
            {
                _bestPieces = new Piece[slotCount];
                _bestColourIds = new int[slotCount];
            }
        }

        private static int WeightFor(int cellCount)
        {
            if (cellCount <= 2)
            {
                return 10;
            }

            if (cellCount == 3)
            {
                return 8;
            }

            if (cellCount == 4)
            {
                return 6;
            }

            if (cellCount == 5)
            {
                return 3;
            }

            return 1;
        }
    }
}
