namespace MustyBlockBlast.Core
{
    /// <summary>
    /// The extra behaviour one <em>dock piece</em> carries on top of the shape it offers. Strictly
    /// orthogonal to that shape: every kind below is offered on the ordinary 1x1
    /// (<see cref="PieceCatalog.SingleCell"/>), so nothing about placement legality, fit search or
    /// scoring reads this enum — it only says what the piece does <em>in addition to</em>, or
    /// <em>instead of</em>, being dropped on the board.
    /// <para>
    /// Deliberately not <see cref="SpecialCellKind"/>, which is the same idea one layer down: that one
    /// tags a cell already standing on the board and fires when the cell is <em>destroyed</em>, this one
    /// tags a piece still sitting in the dock and fires when the piece is <em>used</em>. A piece kind
    /// never survives being played — the tray slot resets to <see cref="None"/> the moment it is
    /// consumed.
    /// </para>
    /// <para>
    /// Every kind is temporary and run-bound: it is injected into a dock slot when its trigger fires,
    /// never entered into an inventory, never persisted and never purchasable, so nothing here is
    /// serialised and a run always opens with three plain pieces.
    /// </para>
    /// </summary>
    public enum SpecialPieceKind
    {
        /// <summary>An ordinary drawn piece. Placing it does exactly what its shape says.</summary>
        None = 0,

        /// <summary>
        /// A "golden" 1x1, earned at a combo streak of five. Behaves exactly as a plain 1x1 does —
        /// it fills the cell it is dropped on and clears that cell's row and/or column if the fill
        /// completed them — which is the same fill-and-conditional-clear rule the Joker power-up
        /// applies (<see cref="JokerFillResolver"/>). The tag is therefore cosmetic to the rules and
        /// exists so the dock can show the player what they earned.
        /// </summary>
        Golden = 1,

        /// <summary>
        /// A "piercing rocket" 1x1, earned by clearing three or more lines in a single move. Placed, it
        /// occupies its cell as normal and then wipes the <em>full</em> row and the <em>full</em> column
        /// running through that cell — always both axes, whether or not either line was complete — and
        /// goes up with them. See <see cref="PiercingRocketEffect"/>, which owns that wipe.
        /// </summary>
        PiercingRocket = 2,

        /// <summary>
        /// A "demolition hammer", injected as a last-resort life-line when the board is at least 90%
        /// full and no legal move is left. The one kind that is never dropped on the board: it is armed
        /// from its dock slot and aimed at a single occupied cell, which it destroys, and is consumed by
        /// that use — so placement is refused for a slot carrying it.
        /// </summary>
        DemolitionHammer = 3,
    }
}
