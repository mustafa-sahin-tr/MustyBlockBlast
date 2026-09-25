using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// Published by the fly-in once a newly spawned special cell's icon has landed on the board — or at
    /// once, when that flight is skipped. The special-cell twin of
    /// <see cref="PowerUpGrantAnimationCompletedMessage"/>: <see cref="Systems.InfoPopupSystem"/> waits
    /// for it before opening a first-time explainer, so the card never covers the cell mid-flight and
    /// never opens before the cell is on the board (issue #464).
    /// </summary>
    public readonly struct SpecialCellFlightCompletedMessage
    {
        public SpecialCellFlightCompletedMessage(SpecialCellKind kind)
        {
            Kind = kind;
        }

        public SpecialCellKind Kind { get; }
    }
}
