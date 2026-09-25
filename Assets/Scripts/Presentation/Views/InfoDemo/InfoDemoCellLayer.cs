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

        /// <summary>A reinforced cell's armour skin (<c>CellView.SetLockedOverlay</c>, issue #438). Value:
        /// hits left, which is the skin's stage count; the element's <see cref="InfoDemoElement.Variant"/>
        /// is the skin (0..<c>Board.LOCKED_SKIN_COUNT</c>-1).</summary>
        Armour,

        /// <summary>A <c>SpecialCellKind.Timer</c> cell: its icon and glow halo
        /// (<c>CellView.SetSpecialIcon</c>/<c>SetSpecialGlow</c>) and its countdown number drawn on the cell
        /// (<c>CellView.SetTimerCountdown</c>, issue #307). Value: placements left.</summary>
        Timer,

        /// <summary>A <c>SpecialCellKind.Diamond</c> gem (<c>DiamondVisuals.Apply</c>, issue #395) — icon
        /// and halo in the gem's own theme colour. Value: the gem's colour id (1..5).</summary>
        Diamond,
    }
}
