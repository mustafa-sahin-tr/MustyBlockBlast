using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>Published only when a placement cleared at least one line.</summary>
    public readonly struct LinesClearedMessage
    {
        public LinesClearedMessage(IReadOnlyList<int> rows, IReadOnlyList<int> columns, int clearedCellCount)
        {
            Rows = rows;
            Columns = columns;
            ClearedCellCount = clearedCellCount;
        }

        public IReadOnlyList<int> Rows { get; }

        public IReadOnlyList<int> Columns { get; }

        public int ClearedCellCount { get; }

        public int LineCount => Rows.Count + Columns.Count;
    }
}
