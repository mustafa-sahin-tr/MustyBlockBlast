using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published once, right after a <see cref="SpecialPieceKind"/> other than
    /// <see cref="SpecialPieceKind.None"/> is injected into a dock slot — never on the Hold-pocket swap,
    /// which only moves an existing piece rather than creating one.
    /// <see cref="MustyBlockBlast.Gameplay.Systems.TutorialSystem"/> is the one subscriber today: it
    /// decides whether the player has already seen this kind's coach-mark and, if not, queues one
    /// pointed at <see cref="SlotIndex"/>.
    /// </summary>
    public readonly struct SpecialPieceSpawnedMessage
    {
        public SpecialPieceSpawnedMessage(SpecialPieceKind kind, int slotIndex)
        {
            Kind = kind;
            SlotIndex = slotIndex;
        }

        public SpecialPieceKind Kind { get; }

        public int SlotIndex { get; }
    }
}
