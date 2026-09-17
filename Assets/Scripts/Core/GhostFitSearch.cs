using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>One placement the Ghost Fit search found: which dock slot to play and where. The
    /// remaining fields are why it won, kept so a caller can show or test the ranking without
    /// re-deriving it.</summary>
    public readonly struct GhostFitMove : IEquatable<GhostFitMove>
    {
        public GhostFitMove(int slotIndex, GridPosition anchor, int lineCount, int largestOpenRegion)
        {
            SlotIndex = slotIndex;
            Anchor = anchor;
            LineCount = lineCount;
            LargestOpenRegion = largestOpenRegion;
        }

        /// <summary>Index into the dock list the search was given — a tray slot index.</summary>
        public int SlotIndex { get; }

        /// <summary>Board anchor the piece is placed at.</summary>
        public GridPosition Anchor { get; }

        /// <summary>Simultaneous rows + columns this placement would clear.</summary>
        public int LineCount { get; }

        /// <summary>Size of the biggest connected empty region left once this placement and any clears
        /// it triggers have resolved.</summary>
        public int LargestOpenRegion { get; }

        public bool Equals(GhostFitMove other)
            => SlotIndex == other.SlotIndex && Anchor.Equals(other.Anchor)
                && LineCount == other.LineCount && LargestOpenRegion == other.LargestOpenRegion;

        public override bool Equals(object obj) => obj is GhostFitMove other && Equals(other);

        public override int GetHashCode()
            => unchecked((((SlotIndex * 397) ^ Anchor.GetHashCode()) * 397) ^ LineCount);

        public override string ToString()
            => $"slot {SlotIndex} at {Anchor} (lines {LineCount}, open {LargestOpenRegion})";
    }

    /// <summary>
    /// The exact search behind the Ghost Fit power-up: every dock piece against every board anchor,
    /// ranked, best one wins. There is no heuristic and no pruning — the answer really is the optimum
    /// over the whole space. The board's own dimensions bound the anchor scan, so the standard 8x8
    /// board and three pieces still cap it at 192 candidate placements; a shape with holes only ever
    /// makes it cheaper, because a hole fails the legality check before anything is projected.
    /// <para>
    /// Ranking, in strict priority order:
    /// </para>
    /// <list type="number">
    /// <item>most simultaneous lines cleared;</item>
    /// <item>combo preservation — among placements tied on (1), one that clears at least one line beats
    /// one that clears none while the streak is running;</item>
    /// <item>most remaining contiguous open cells, measured on the board as it stands <em>after</em> the
    /// placement and any clears it triggers have resolved (a two-line clear leaves a far more open board
    /// than the footprint alone suggests, and criterion 3 is meant to reward exactly that).</item>
    /// </list>
    /// <para>
    /// Criterion (2) is, as written, unreachable: (1) is a strict maximisation, so two candidates tied on
    /// it clear the <em>same</em> line count, and "clears >= 1" is then either true of both or false of
    /// both. It is implemented as its own explicit ranking term anyway — ordered between (1) and (3) —
    /// so the behaviour the acceptance criterion describes is structurally guaranteed rather than merely
    /// not contradicted, and so loosening (1) later cannot silently drop combo preservation.
    /// </para>
    /// <para>
    /// Ties that survive all three are broken by scan order — dock slot ascending, then row, then column
    /// — so the same board and dock always produce the same suggestion. A stable answer matters here:
    /// the suggestion sits on screen and must not flicker between equally good moves.
    /// </para>
    /// <para>
    /// An instance owns its scratch buffers and reuses them on every call, in the same shape
    /// <c>BoardSystem</c>'s drag-preview query uses, so a search allocates nothing however often the
    /// player asks for one.
    /// </para>
    /// </summary>
    public sealed class GhostFitSearch
    {
        /// <summary>Arbitrary non-empty colour the projection stamps the piece with. Colour never affects
        /// placement or clearing, and nothing reads this board back — only its empty/occupied shape.</summary>
        private const int PROJECTION_COLOUR_ID = 1;

        private readonly List<int> _rowsBuffer = new List<int>(Board.SIZE);
        private readonly List<int> _columnsBuffer = new List<int>(Board.SIZE);

        /// <summary>Scratch space for the cells of the lines a projected placement clears, handed to
        /// <see cref="LineClearResolver.ApplyClearedLines"/>. Owned and reused, so projecting a
        /// candidate placement allocates nothing.</summary>
        private readonly List<GridPosition> _projectionCellBuffer = new List<GridPosition>(Board.SIZE * 2);

        /// <summary>
        /// Scratch state, rebuilt only when the board being searched has a different shape from the one
        /// the last search saw. An instance of this class outlives any single level, so it cannot be
        /// sized at construction time against a shape it has not been shown yet — but a shape changes
        /// at most once per level and never mid-drag, so "grow when the shape changes, reuse otherwise"
        /// keeps every repeated search on the same board allocation-free, which is the property the
        /// caller-owned-buffer design exists to give.
        /// </summary>
        private BoardShape _scratchShape;
        private Board _previewBoard;
        private Board _projectionBoard;
        private bool[] _visitedBuffer = new bool[Board.SIZE * Board.SIZE];
        private int[] _stackBuffer = new int[Board.SIZE * Board.SIZE];

        /// <summary>
        /// Finds the best placement over <paramref name="dockPieces"/> and every board anchor. Null
        /// entries in the list are skipped, so a partly played-out dock is handed straight in.
        /// <para>
        /// Returns false — with <paramref name="bestMove"/> left at its default — when no piece has a
        /// legal anchor anywhere. That is the "no placements possible" answer, reported rather than
        /// thrown: the caller surfaces it to the player.
        /// </para>
        /// </summary>
        /// <param name="isStreakActive">Whether the run's combo streak is currently running, which is
        /// the only thing that makes ranking criterion (2) apply at all.</param>
        public bool TryFindBestMove(
            Board board, IReadOnlyList<Piece> dockPieces, bool isStreakActive, out GhostFitMove bestMove)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (dockPieces == null)
            {
                throw new ArgumentNullException(nameof(dockPieces));
            }

            EnsureScratchFor(board);

            bestMove = default;
            bool hasBest = false;
            bool bestPreservesStreak = false;

            for (int slotIndex = 0; slotIndex < dockPieces.Count; slotIndex++)
            {
                Piece piece = dockPieces[slotIndex];
                if (piece == null)
                {
                    continue;
                }

                for (int y = 0; y < board.Height; y++)
                {
                    for (int x = 0; x < board.Width; x++)
                    {
                        var anchor = new GridPosition(x, y);
                        if (!PlacementRules.CanPlace(board, piece, anchor))
                        {
                            continue;
                        }

                        // The same non-mutating primitive the drag preview's would-clear highlight uses,
                        // so "how many lines does this clear" has exactly one definition in the project.
                        LineClearResult clears = LineClearResolver.PreviewClears(
                            board, piece, anchor, _previewBoard, _rowsBuffer, _columnsBuffer);

                        bool preservesStreak = isStreakActive && clears.LineCount > 0;
                        int openRegion = ProjectLargestOpenRegion(board, piece, anchor, clears);

                        if (hasBest && !IsBetter(
                                clears.LineCount, preservesStreak, openRegion,
                                bestMove.LineCount, bestPreservesStreak, bestMove.LargestOpenRegion))
                        {
                            continue;
                        }

                        bestMove = new GhostFitMove(slotIndex, anchor, clears.LineCount, openRegion);
                        bestPreservesStreak = preservesStreak;
                        hasBest = true;
                    }
                }
            }

            return hasBest;
        }

        /// <summary>Points the scratch boards and flood-fill buffers at <paramref name="board"/>'s
        /// shape, rebuilding them only when that shape is not the one they were last built for. The
        /// buffers are never shrunk — a search that moves between two shapes repeatedly should keep
        /// whichever is bigger rather than reallocate each way.</summary>
        private void EnsureScratchFor(Board board)
        {
            if (!ReferenceEquals(_scratchShape, board.Shape))
            {
                _scratchShape = board.Shape;
                _previewBoard = new Board(_scratchShape);
                _projectionBoard = new Board(_scratchShape);
            }

            if (_visitedBuffer.Length < board.CellCount)
            {
                _visitedBuffer = new bool[board.CellCount];
                _stackBuffer = new int[board.CellCount];
            }
        }

        /// <summary>The three ranking criteria, applied in order. Strictly greater on purpose: an
        /// all-round tie leaves the incumbent in place, which is what makes scan order the tie-break.</summary>
        private static bool IsBetter(
            int lineCount, bool preservesStreak, int openRegion,
            int bestLineCount, bool bestPreservesStreak, int bestOpenRegion)
        {
            if (lineCount != bestLineCount)
            {
                return lineCount > bestLineCount;
            }

            if (preservesStreak != bestPreservesStreak)
            {
                return preservesStreak;
            }

            return openRegion > bestOpenRegion;
        }

        /// <summary>
        /// The board this placement leaves behind — piece stamped in, then every line it completed
        /// emptied — measured for its largest contiguous empty region.
        /// <para>
        /// Rebuilt on its own scratch board rather than reading whatever
        /// <see cref="LineClearResolver.PreviewClears"/> happened to leave in <see cref="_previewBoard"/>:
        /// that board is the preview's private workspace and its post-call contents are not part of the
        /// contract. Copying 64 ints per candidate is far cheaper than a rule the two could drift on.
        /// </para>
        /// </summary>
        private int ProjectLargestOpenRegion(
            Board board, Piece piece, GridPosition anchor, LineClearResult clears)
        {
            _projectionBoard.CopyFrom(board);
            PlacementRules.Place(_projectionBoard, piece, anchor, PROJECTION_COLOUR_ID);

            // Through the resolver's own line-emptying primitive, so the projection agrees with what the
            // real clear would do — including leaving a reinforced cell standing where it would only
            // have been damaged, which is the difference between an honest open-region figure and an
            // optimistic one.
            LineClearResolver.ApplyClearedLines(
                _projectionBoard, clears.ClearedRows, clears.ClearedColumns, _projectionCellBuffer);

            return _projectionBoard.LargestEmptyRegionSize(_visitedBuffer, _stackBuffer);
        }
    }
}
