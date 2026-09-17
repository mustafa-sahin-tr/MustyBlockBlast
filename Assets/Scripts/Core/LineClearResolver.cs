using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>Result of resolving line clears for one placement. Rows and columns are cleared
    /// simultaneously; a cell at their intersection is counted once.</summary>
    public readonly struct LineClearResult
    {
        public LineClearResult(
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            int clearedCellCount,
            int monochromeLineCount)
            : this(
                clearedRows, clearedColumns, clearedCellCount, monochromeLineCount,
                reinforcedCellsFullyClearedCount: 0)
        {
        }

        public LineClearResult(
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            int clearedCellCount,
            int monochromeLineCount,
            int reinforcedCellsFullyClearedCount)
            : this(
                clearedRows, clearedColumns, clearedCellCount, monochromeLineCount,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour: null)
        {
        }

        public LineClearResult(
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            int clearedCellCount,
            int monochromeLineCount,
            int reinforcedCellsFullyClearedCount,
            IReadOnlyList<int> destroyedCellCountByColour)
        {
            ClearedRows = clearedRows;
            ClearedColumns = clearedColumns;
            ClearedCellCount = clearedCellCount;
            MonochromeLineCount = monochromeLineCount;
            ReinforcedCellsFullyClearedCount = reinforcedCellsFullyClearedCount;
            DestroyedCellCountByColour = destroyedCellCountByColour;
        }

        public IReadOnlyList<int> ClearedRows { get; }

        public IReadOnlyList<int> ClearedColumns { get; }

        /// <summary>Total distinct cells emptied, counting each row/column intersection once. A
        /// reinforced cell this clear only damaged is <em>not</em> counted: it is still standing, so it
        /// scores nothing and was not emptied (issue #153 AC3).</summary>
        public int ClearedCellCount { get; }

        /// <summary>How many of the cleared lines consisted entirely of one colour. Each row/column is
        /// evaluated independently, so a shared intersection cell never forces the two lines to match.</summary>
        public int MonochromeLineCount { get; }

        /// <summary>
        /// Of <see cref="ClearedCellCount"/>, how many were reinforced cells taking their last hit —
        /// as distinct from ordinary cells that were never reinforced at all.
        /// <para>
        /// Data plumbing for issue #154's "clear all reinforced cells" objective, which needs to tell
        /// the two apart; nothing reads it as an objective yet. Threaded on to
        /// <c>PiecePlacedMessage.ReinforcedCellsFullyClearedCount</c> exactly as
        /// <see cref="MonochromeLineCount"/> is threaded on to that message's own copy.
        /// </para>
        /// </summary>
        public int ReinforcedCellsFullyClearedCount { get; }

        /// <summary>
        /// How many cells of each colour this clear destroyed, indexed by colour id — see
        /// <see cref="ColourTally"/>. Each intersection cell is counted once, matching
        /// <see cref="ClearedCellCount"/>, and a reinforced cell that only took a hit is not in it.
        /// Null for a preview, which stamps a placeholder colour and so cannot tally honestly.
        /// </summary>
        public IReadOnlyList<int> DestroyedCellCountByColour { get; }

        /// <summary>Simultaneous lines cleared — the "lines" term used by <see cref="ScoreRules"/>.</summary>
        public int LineCount => ClearedRows.Count + ClearedColumns.Count;

        public bool AnyCleared => LineCount > 0;
    }

    /// <summary>Finds and clears every full row/column on the board. Must be run once, after the
    /// whole piece has been placed — never mid-placement.</summary>
    public static class LineClearResolver
    {
        private const int PreviewColourId = 1; // Arbitrary non-EMPTY id; colour is cosmetic and never affects placement/clearing.

        /// <summary>Scratch space for <see cref="PreviewClears"/>'s candidate cells. Owned and reused
        /// rather than allocated per call, because the preview runs every drag-update frame and must
        /// stay allocation-free; it never escapes a <see cref="PreviewClears"/> call.</summary>
        private static readonly List<GridPosition> PreviewCellBuffer =
            new List<GridPosition>(Board.SIZE * 2);

        /// <summary>Non-mutating: what would clear if <paramref name="piece"/> were placed at
        /// <paramref name="anchor"/> on <paramref name="board"/>, without touching <paramref name="board"/>.
        /// Uses <paramref name="scratchBoard"/> as scratch space (its contents are overwritten) and
        /// <paramref name="resultRows"/>/<paramref name="resultColumns"/> as the result buffers (cleared then
        /// repopulated) so this can be called every drag-update frame with zero allocation.</summary>
        public static LineClearResult PreviewClears(
            Board board,
            Piece piece,
            GridPosition anchor,
            Board scratchBoard,
            List<int> resultRows,
            List<int> resultColumns)
        {
            resultRows.Clear();
            resultColumns.Clear();

            if (!PlacementRules.CanPlace(board, piece, anchor))
            {
                return new LineClearResult(resultRows, resultColumns, 0, 0);
            }

            scratchBoard.CopyFrom(board);
            for (int i = 0; i < piece.Offsets.Count; i++)
            {
                scratchBoard.Occupy(anchor + piece.Offsets[i], PreviewColourId);
            }

            for (int y = 0; y < scratchBoard.Height; y++)
            {
                if (scratchBoard.IsRowFull(y))
                {
                    resultRows.Add(y);
                }
            }

            for (int x = 0; x < scratchBoard.Width; x++)
            {
                if (scratchBoard.IsColumnFull(x))
                {
                    resultColumns.Add(x);
                }
            }

            // Counted off the real candidate cells rather than from a width/height formula, so a
            // reinforced cell that would only be damaged is excluded from the hint exactly as it is
            // excluded from the real clear (AC3). Non-mutating: the scratch board is read, never
            // damaged.
            CollectDestroyedCells(scratchBoard, resultRows, resultColumns, PreviewCellBuffer);
            int clearedCellCount = CountRemovableCells(scratchBoard, PreviewCellBuffer);

            // Always 0: the previewed piece is stamped with the placeholder PreviewColourId rather than its
            // real colour, so per-line colour uniformity cannot be judged here. No UI surfaces it yet.
            return new LineClearResult(resultRows, resultColumns, clearedCellCount, 0);
        }

        /// <summary>Clears every currently-full row and column, once. Deliberately single-pass: it
        /// resolves the board exactly as it stands and never chains. Chaining is
        /// <see cref="CascadeClearResolver"/>'s job, which layers on top of this and is used only for a
        /// real placement — a drag preview must keep seeing "what is full right now", nothing more.</summary>
        public static LineClearResult ResolveClears(Board board)
            => ResolveClears(board, null, null);

        /// <summary>
        /// As <see cref="ResolveClears(Board)"/>, additionally reporting what this pass destroyed.
        /// Both buffers are optional (pass null to skip that bookkeeping) and are cleared before use.
        /// <para>
        /// <paramref name="destroyedCells"/> receives each distinct emptied cell exactly once — a
        /// cleared row/column intersection is listed by the row pass only, matching the
        /// "count intersections once" rule <see cref="LineClearResult.ClearedCellCount"/> uses. A
        /// reinforced cell this clear merely damaged is not among them: it is still standing.
        /// <paramref name="triggeredSpecials"/> receives the special cells among them, collected
        /// through <see cref="SpecialCellDetection"/> after the damage gate but before anything is
        /// cleared — the one moment the list is final and the kinds are still readable.
        /// </para>
        /// <para>
        /// Internal because only <see cref="CascadeClearResolver"/> needs it: it exists so the cascade
        /// does not re-implement "find the full lines and clear them", which is the one behaviour AC3
        /// requires to stay bit-identical.
        /// </para>
        /// </summary>
        internal static LineClearResult ResolveClears(
            Board board, List<GridPosition> destroyedCells, List<SpecialCellTrigger> triggeredSpecials)
        {
            // Validated up front, before anything is read, written or handed back: detection reads the
            // destroyed-cell list, so the two buffers are supplied together or not at all rather than
            // this method quietly allocating a hidden one. Checking here rather than at the point of
            // use means a caller that got the pairing wrong never sees a half-processed buffer.
            if (triggeredSpecials != null && destroyedCells == null)
            {
                throw new ArgumentException(
                    "A destroyed-cell buffer is required to collect triggered specials.",
                    nameof(destroyedCells));
            }

            var clearedRows = new List<int>();
            var clearedColumns = new List<int>();

            for (int y = 0; y < board.Height; y++)
            {
                if (board.IsRowFull(y))
                {
                    clearedRows.Add(y);
                }
            }

            for (int x = 0; x < board.Width; x++)
            {
                if (board.IsColumnFull(x))
                {
                    clearedColumns.Add(x);
                }
            }

            // Must run before any clearing — once cleared, the colour data is gone.
            int monochromeLineCount = 0;
            for (int i = 0; i < clearedRows.Count; i++)
            {
                if (IsRowMonochrome(board, clearedRows[i]))
                {
                    monochromeLineCount++;
                }
            }

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                if (IsColumnMonochrome(board, clearedColumns[i]))
                {
                    monochromeLineCount++;
                }
            }

            // The geometric candidates — every playable cell of every cleared line, each intersection
            // listed once. Built into the caller's buffer when it wants one, so there is exactly one
            // list and the caller's "what did this destroy" report is the very list that gets damaged.
            List<GridPosition> candidates = destroyedCells ?? new List<GridPosition>(Board.SIZE * 2);
            CollectDestroyedCells(board, clearedRows, clearedColumns, candidates);

            // The damage gate, fused into turning the candidates into the real destroyed-cell list: a
            // reinforced cell with hits to spare absorbs one here and is dropped from the list, so it
            // counts towards neither ClearedCellCount nor the triggers collected below, and can never
            // be reported as destroyed (AC3). Each cell is visited exactly once however many of the
            // cleared lines pass through it, because CollectDestroyedCells already de-duplicated the
            // intersections — which is what makes a row-and-column clear cost one hit, not two (AC4).
            int reinforcedCellsFullyClearedCount = ReinforcedCellDamage.SpendHits(board, candidates);

            // On the post-gate list, so a reinforced cell that survived is not counted as destroyed;
            // and before the removal below, because Board.Clear wipes the colour this reads.
            int[] destroyedCellCountByColour = ColourTally.Count(board, candidates);

            if (triggeredSpecials != null)
            {
                triggeredSpecials.Clear();

                // On the post-gate list, never on the raw candidates: a cell that survived was not
                // destroyed and owes no effect. Still before the removal below, because Board.Clear
                // wipes a cell's kind along with its colour.
                //
                // The cleared lines are passed along so each trigger records which of them destroyed
                // it: an effect that fires relative to that line (a laser) needs to know, and this is
                // the only point where "this cell went with that row/column" is still known.
                SpecialCellDetection.CollectTriggered(
                    board, candidates, triggeredSpecials, clearedRows, clearedColumns);
            }

            ReinforcedCellDamage.RemoveAll(board, candidates);

            // Read off the cells actually removed rather than recomputed from the lines' geometry: the
            // two agreed exactly before reinforced cells existed (a full line's playable cells are all
            // occupied by definition), and they must keep agreeing now that a line can clear with one
            // of its cells left standing.
            return new LineClearResult(
                clearedRows, clearedColumns, candidates.Count, monochromeLineCount,
                reinforcedCellsFullyClearedCount, destroyedCellCountByColour);
        }

        /// <summary>
        /// Empties the given already-identified cleared lines on <paramref name="board"/>, through the
        /// same damage gate a real clear uses — so a reinforced cell on one of them spends a hit and
        /// stays standing here too.
        /// <para>
        /// Internal, for <see cref="GhostFitSearch"/>, which stamps a candidate placement onto a
        /// scratch board and needs the post-clear board to measure. It replaced a pair of
        /// "empty this row"/"empty this column" helpers: those cleared unconditionally, and a second
        /// definition of "clear this line" is exactly what must not exist now that clearing one is
        /// conditional. <paramref name="cellBuffer"/> is supplied by the caller (its contents on entry
        /// are irrelevant) because that caller runs this once per candidate placement and must not
        /// allocate per call.
        /// </para>
        /// </summary>
        internal static void ApplyClearedLines(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            List<GridPosition> cellBuffer)
        {
            CollectDestroyedCells(board, clearedRows, clearedColumns, cellBuffer);
            ReinforcedCellDamage.SpendHits(board, cellBuffer);
            ReinforcedCellDamage.RemoveAll(board, cellBuffer);
        }

        /// <summary>How many of <paramref name="cells"/> a clear would actually remove: the ones with no
        /// hit left to absorb. Read-only — the non-mutating counterpart to
        /// <see cref="ReinforcedCellDamage.SpendHits"/>, for the preview path, which must answer "how
        /// much would this clear" without touching the board.</summary>
        private static int CountRemovableCells(Board board, IReadOnlyList<GridPosition> cells)
        {
            int count = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                if (board.GetHitCount(cells[i]) <= 1)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Fills <paramref name="results"/> with every distinct cell the given lines cover.
        /// Column cells sitting in an already-listed row are skipped, so an intersection appears once.
        /// The cells of a line come from the board itself rather than a <see cref="Board.SIZE"/> loop
        /// here, so a board with a different shape has one place to change.</summary>
        private static void CollectDestroyedCells(
            Board board,
            IReadOnlyList<int> clearedRows,
            IReadOnlyList<int> clearedColumns,
            List<GridPosition> results)
        {
            results.Clear();

            for (int i = 0; i < clearedRows.Count; i++)
            {
                board.CollectRowCells(clearedRows[i], results);
            }

            int cellCountFromRows = results.Count;

            for (int i = 0; i < clearedColumns.Count; i++)
            {
                board.CollectColumnCells(clearedColumns[i], results);
            }

            // Walk only the cells the column pass just appended and drop the ones a cleared row
            // already contributed. Compacting in place beats a HashSet: this runs once per placement
            // over at most a few dozen cells, and allocates nothing.
            int writeIndex = cellCountFromRows;
            for (int readIndex = cellCountFromRows; readIndex < results.Count; readIndex++)
            {
                if (ContainsIndex(clearedRows, results[readIndex].Y))
                {
                    continue;
                }

                results[writeIndex] = results[readIndex];
                writeIndex++;
            }

            results.RemoveRange(writeIndex, results.Count - writeIndex);
        }

        private static bool ContainsIndex(IReadOnlyList<int> indices, int value)
        {
            for (int i = 0; i < indices.Count; i++)
            {
                if (indices[i] == value)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when every playable cell of row <paramref name="y"/> holds the same non-empty
        /// colour. Holes carry no colour and are skipped rather than counted as a mismatch.</summary>
        private static bool IsRowMonochrome(Board board, int y)
        {
            int firstColourId = Board.EMPTY;

            for (int x = 0; x < board.Width; x++)
            {
                var position = new GridPosition(x, y);
                if (board.IsHole(position))
                {
                    continue;
                }

                int colourId = board[position];
                if (colourId == Board.EMPTY)
                {
                    return false;
                }

                if (firstColourId == Board.EMPTY)
                {
                    firstColourId = colourId;
                    continue;
                }

                if (colourId != firstColourId)
                {
                    return false;
                }
            }

            return firstColourId != Board.EMPTY;
        }

        /// <summary>True when every playable cell of column <paramref name="x"/> holds the same
        /// non-empty colour.</summary>
        private static bool IsColumnMonochrome(Board board, int x)
        {
            int firstColourId = Board.EMPTY;

            for (int y = 0; y < board.Height; y++)
            {
                var position = new GridPosition(x, y);
                if (board.IsHole(position))
                {
                    continue;
                }

                int colourId = board[position];
                if (colourId == Board.EMPTY)
                {
                    return false;
                }

                if (firstColourId == Board.EMPTY)
                {
                    firstColourId = colourId;
                    continue;
                }

                if (colourId != firstColourId)
                {
                    return false;
                }
            }

            return firstColourId != Board.EMPTY;
        }
    }
}
