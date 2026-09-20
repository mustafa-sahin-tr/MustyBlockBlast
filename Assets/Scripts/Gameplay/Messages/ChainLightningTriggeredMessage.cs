namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="MustyBlockBlast.Core.SpecialCellKind.ChainLightning"/> cells fired and
    /// their strikes vaporized <see cref="VaporizedCellCount"/> cells. Published by whichever System
    /// resolved the destruction — a placement's cascade or a spent power-up — so a strike reads the same
    /// to every subscriber whatever set it off.
    /// <para>
    /// A count, exactly as <see cref="LaserFiredMessage"/> is, and for the same reason: the cells it
    /// destroyed already reached Presentation through the ordinary "this cell is empty now" path, so a
    /// number is enough to tell it to sweep them. Only <see cref="VortexIslandFilledMessage"/> has to carry
    /// positions, because a move is the one thing a count cannot describe.
    /// </para>
    /// <para>
    /// Published only when a strike actually emptied something — a tile that fired on a board with
    /// nothing left standing is not an event, so subscribers never have to handle a zero count.
    /// </para>
    /// </summary>
    public readonly struct ChainLightningTriggeredMessage
    {
        public ChainLightningTriggeredMessage(int vaporizedCellCount)
        {
            VaporizedCellCount = vaporizedCellCount;
        }

        /// <summary>How many occupied cells the strike (and any chained strike) emptied. Never counts a
        /// cell that was already empty, and never counts the firing tile's own cell — that was emptied by
        /// whatever destroyed it, and is already reported by that clear.</summary>
        public int VaporizedCellCount { get; }
    }
}
