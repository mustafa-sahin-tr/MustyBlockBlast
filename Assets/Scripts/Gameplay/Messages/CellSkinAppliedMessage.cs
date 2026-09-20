using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once per board cell a score-threshold conversion just painted with a
    /// <see cref="CellSkinKind"/> other than <see cref="CellSkinKind.None"/> (issue #324, sub-issue A).
    /// Mirrors <see cref="SpecialCellSpawnedMessage"/> exactly: never published for a skin being
    /// cleared or consumed, only for genuine creation via <see cref="MustyBlockBlast.Gameplay.Systems.CellSkinSystem"/>'s
    /// conversion batch.
    /// </summary>
    public readonly struct CellSkinAppliedMessage
    {
        public CellSkinAppliedMessage(CellSkinKind kind, GridPosition position)
        {
            Kind = kind;
            Position = position;
        }

        public CellSkinKind Kind { get; }

        public GridPosition Position { get; }
    }
}
