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

        /// <summary>A solid rounded rectangle (a progress chip, a badge disc) — strip furniture.
        /// In front of the glows, behind the icons, so a sprite can sit on a panel.</summary>
        Panel,

        /// <summary>A sprite — a special cell's icon, a power-up icon, a finger.</summary>
        Icon,

        /// <summary>A solid hollow rounded frame (a tap ring, a power-up's "armed" ring, issue #448).
        /// In front of the icons, so a ring expanding from a fingertip stays visible over it.</summary>
        Ring,

        /// <summary>A piece made of several blocks (tray pieces, a piece being dragged).</summary>
        Piece,

        /// <summary>A localized floating label.</summary>
        Label,
    }
}
