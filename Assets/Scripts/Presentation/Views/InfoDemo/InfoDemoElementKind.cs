namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// What an <see cref="InfoDemoElement"/> is drawn as. Each kind has its own pooled visual on the
    /// <see cref="InfoDemoStage"/>, and its own layer there (listed here back to front).
    /// </summary>
    internal enum InfoDemoElementKind
    {
        /// <summary>One of the 64 mini-board blocks — always ids 0..63 (see
        /// <see cref="InfoDemoLayout.BoardBlockId"/>). Hidden while its paint is
        /// <see cref="InfoDemoPaint.NONE"/>, showing the empty cell drawn under it.</summary>
        BoardBlock,

        /// <summary>A soft rounded glow band (a would-clear row/column highlight). Behind the blocks.</summary>
        Band,

        /// <summary>A dashed rounded outline around a group of cells.</summary>
        Outline,

        /// <summary>A soft radial glow — a burst, or the halo behind a special cell's icon.</summary>
        Glow,

        /// <summary>A sprite — a special cell's icon, a power-up icon, a finger.</summary>
        Icon,

        /// <summary>A piece made of several blocks (tray pieces, a piece being dragged).</summary>
        Piece,

        /// <summary>A localized floating label.</summary>
        Label,
    }
}
