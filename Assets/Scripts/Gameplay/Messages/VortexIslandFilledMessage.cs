using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A <see cref="SpecialCellKind.Vortex"/> cell was destroyed: either it reclaimed every fully-enclosed
    /// island of empty cells the board had (<see cref="FilledCells"/>), or — when it found none — it
    /// handed its tag to another cell (<see cref="HandOffTargets"/>). Published by whichever System
    /// resolved the destruction, so the event reads the same to every subscriber whatever set it off.
    /// <para>
    /// The two lists are mutually exclusive per vortex destroyed: a single trigger either fills whatever
    /// islands it found or hands off, never both (issue #349). Both can be non-empty on the one message
    /// when a resolution destroys more than one vortex — one might fill while another, finding the board
    /// already clear of islands, hands off — which is why this reports the whole resolution's vortex work
    /// rather than one trigger's.
    /// </para>
    /// <para>
    /// <see cref="FilledCells"/> carries every reclaimed cell outright rather than a count: those cells'
    /// new colour already reached Presentation through the ordinary "this cell changed" path by the time
    /// this publishes, so the list exists purely to say <em>which</em> cells to play the fill-in animation
    /// on. <see cref="HandOffTargets"/> exists for the matching reason on the hand-off side: the icon
    /// change already reached Presentation through the ordinary special-kind path, and this says which
    /// cell to play the hand-off's own animation on.
    /// </para>
    /// <para>
    /// Both lists are handed over outright and never written to again, so a subscriber that animates
    /// them over several frames may keep them. Published once per resolution, not per frame, so the copy
    /// that guarantees this costs nothing that matters.
    /// </para>
    /// <para>
    /// Published only when at least one vortex actually did something — a resolution with no vortex
    /// destroyed publishes nothing at all, so subscribers never have to handle two empty lists.
    /// </para>
    /// </summary>
    public readonly struct VortexIslandFilledMessage
    {
        public VortexIslandFilledMessage(
            IReadOnlyList<GridPosition> filledCells, IReadOnlyList<GridPosition> handOffTargets)
        {
            FilledCells = filledCells;
            HandOffTargets = handOffTargets;
        }

        /// <summary>Every cell a vortex's fill reclaimed this resolution, in the order the scan found
        /// them. May be empty when every vortex destroyed this resolution instead handed off.</summary>
        public IReadOnlyList<GridPosition> FilledCells { get; }

        /// <summary>Every cell a vortex's hand-off tagged <see cref="SpecialCellKind.Vortex"/> this
        /// resolution. May be empty when every vortex destroyed this resolution found an island to fill
        /// instead.</summary>
        public IReadOnlyList<GridPosition> HandOffTargets { get; }
    }
}
