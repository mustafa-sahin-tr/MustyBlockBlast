using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// A placeable shape, defined as cell offsets from its anchor. Immutable: a piece is never rotated
    /// in place — each orientation is its own definition (see docs/game-design.md).
    /// <para>
    /// The Rotate power-up, the design's one explicit exception to "pieces are not rotatable", does not
    /// change that: it swaps a tray slot's reference to the catalog piece that already describes the
    /// turned shape (see <see cref="PieceRotator"/>), so a piece's <see cref="Id"/> never stops
    /// describing its offsets.
    /// </para>
    /// </summary>
    public sealed class Piece
    {
        private readonly GridPosition[] _offsets;

        public Piece(string id, IReadOnlyList<GridPosition> offsets)
        {
            if (offsets == null || offsets.Count == 0)
            {
                throw new ArgumentException("A piece needs at least one cell.", nameof(offsets));
            }

            Id = id ?? throw new ArgumentNullException(nameof(id));
            _offsets = new GridPosition[offsets.Count];
            for (int i = 0; i < offsets.Count; i++)
            {
                _offsets[i] = offsets[i];
            }
        }

        public string Id { get; }

        public IReadOnlyList<GridPosition> Offsets => _offsets;

        public int CellCount => _offsets.Length;

        public override string ToString() => Id;
    }
}
