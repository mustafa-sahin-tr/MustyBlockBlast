namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Which of a real board cell's special-cell layers an <see cref="InfoDemoElementKind.CellLayer"/>
    /// element draws (issue #453). Each is drawn by a pooled <see cref="CellView"/> through the very call
    /// <see cref="BoardView"/> makes for it, so a demo's ice, armour, timer or diamond is the board's own
    /// art, never a re-authored copy. The element's animatable <see cref="InfoDemoElementState.Paint"/>
    /// carries the layer's value — see each member — and 0 hides the layer.
    /// </summary>
    internal enum InfoDemoCellLayer
    {
        /// <summary>An ice socket (<c>CellView.SetIceOverlay</c>, issue #433): the frost plate and ring.
        /// Value: the ice level left (1..<c>TargetIceCellAuthoring.MAX_ICE_LEVEL</c>). The socket belongs to
        /// the position, so it shows whether or not a block stands on it.</summary>
        IceSocket,

        /// <summary>A reinforced cell's rock (<c>CellView.SetStageOverlay</c>, issues #438/#480). Value:
        /// hits left, which picks the rock's stage (<c>BoardView.ReinforcedStageSprite</c>). The element's
        /// <see cref="InfoDemoElement.Variant"/> (the model's rolled skin) is no longer drawn: every
        /// reinforced cell wears the one rock.</summary>
        Armour,

        /// <summary>A <c>SpecialCellKind.Timer</c> cell: its art, full-bleed (<c>CellView.SetSpecialIcon</c>,
        /// issue #480) and its countdown number drawn on the cell
        /// (<c>CellView.SetTimerCountdown</c>, issue #307). Value: placements left.</summary>
        Timer,

        /// <summary>A <c>SpecialCellKind.Diamond</c> gem (<c>DiamondVisuals.Apply</c>, issue #395) — its
        /// crystal, full-bleed, in the gem's own theme colour (issue #480). Value: the gem's colour id (1..5).</summary>
        Diamond,
    }
}
