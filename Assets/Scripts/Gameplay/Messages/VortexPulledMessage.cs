using System.Collections.Generic;
using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// One or more <see cref="SpecialCellKind.Vortex"/> cells were destroyed and dragged the blocks in
    /// <see cref="Pulls"/> one cell inwards each. Published by whichever System resolved the
    /// destruction, so a pull reads the same to every subscriber whatever set it off.
    /// <para>
    /// Unlike <see cref="ExplosiveCoreDetonatedMessage"/> and <see cref="LaserFiredMessage"/>, which
    /// carry only a count, this carries every move outright. Those two destroy cells, and the cells they
    /// destroyed already reached Presentation through the ordinary "this cell is empty now" path — a
    /// count is enough to tell it to sweep them. A vortex <em>moves</em> blocks, and no subscriber can
    /// reconstruct which block went where from a number, so the pairs are the message.
    /// </para>
    /// <para>
    /// <see cref="Pulls"/> is a list the publisher hands over outright and never writes to again, so a
    /// subscriber that animates the moves over several frames may keep it. Published once per
    /// resolution, not per frame, so the copy that guarantees this costs nothing that matters.
    /// </para>
    /// <para>
    /// Published only when at least one block actually moved — a vortex that found nothing isolated, or
    /// nothing isolated with anywhere to go, is not an event, so subscribers never have to handle an
    /// empty list.
    /// </para>
    /// </summary>
    public readonly struct VortexPulledMessage
    {
        public VortexPulledMessage(IReadOnlyList<VortexPull> pulls)
        {
            Pulls = pulls;
        }

        /// <summary>Every block the vortex moved, each as the cell it left and the cell it now occupies,
        /// in the order they moved. Never empty (see the type's remarks).</summary>
        public IReadOnlyList<VortexPull> Pulls { get; }
    }
}
