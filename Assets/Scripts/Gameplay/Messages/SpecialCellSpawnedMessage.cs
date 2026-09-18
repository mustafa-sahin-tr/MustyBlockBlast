using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once, right after a <see cref="SpecialCellKind"/> other than
    /// <see cref="SpecialCellKind.None"/> is first painted onto a board cell — never on a kind being
    /// cleared or consumed, only on genuine creation. <see cref="MustyBlockBlast.Gameplay.Systems.TutorialSystem"/>
    /// is the one subscriber today: it decides whether the player has already seen this kind's
    /// coach-mark and, if not, queues one pointed at <see cref="Position"/>.
    /// </summary>
    public readonly struct SpecialCellSpawnedMessage
    {
        public SpecialCellSpawnedMessage(SpecialCellKind kind, GridPosition position)
        {
            Kind = kind;
            Position = position;
        }

        public SpecialCellKind Kind { get; }

        public GridPosition Position { get; }
    }
}
