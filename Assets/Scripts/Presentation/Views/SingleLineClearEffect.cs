namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Which visual treatment a single row/column clear plays across every one of its cells, chosen
    /// once per clear event (never per cell) so the whole line reads as one coherent reveal (issue
    /// #331). Layered underneath issue #328's staggered per-cell reveal timing — this changes WHAT a
    /// cell does when its turn comes, not whether cells still reveal staggered left-to-right /
    /// bottom-to-top.
    /// <para>
    /// Presentation-only: Core/Gameplay reports only that cells cleared, never how they should look
    /// clearing. See <c>BoardView.OnLinesCleared</c> and <c>BoardView.OnPowerUpApplied</c> for the two
    /// paths that pick one of these at random per clear event.
    /// </para>
    /// </summary>
    internal enum SingleLineClearEffect
    {
        /// <summary>The cell's fill breaks into a few shards that fly outward and fade, alongside the
        /// ordinary opacity fade already playing on the cell itself.</summary>
        Shatter,

        /// <summary>The cell's fill tints towards an ember colour as it fades, with a couple of small
        /// embers drifting upward off it.</summary>
        Burn,

        /// <summary>The cell itself shrinks and slides towards the nearest board corner while fading,
        /// instead of fading in place.</summary>
        FlyToCorner
    }
}
