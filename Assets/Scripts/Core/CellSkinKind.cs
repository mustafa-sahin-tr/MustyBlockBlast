namespace MustyBlockBlast.Core
{
    /// <summary>
    /// A purely cosmetic decorative overlay a board cell (or a dock piece's cells) can carry, on top of
    /// its colour id and independently of any <see cref="SpecialCellKind"/> it also carries (issue
    /// #324, sub-issue A). Strictly orthogonal to both: a skin never affects placement legality,
    /// line-clear detection, occupancy counts (<see cref="Board.OccupiedCellCount"/>,
    /// <see cref="Board.IsEmpty"/>), scoring, or any <see cref="SpecialCellKind"/> effect. A cell can
    /// carry a skin and a special kind at the same time (e.g. a Candy-themed Coin cell) — both render,
    /// neither is affected by the other.
    /// <para>
    /// Active only in <see cref="MustyBlockBlast.Gameplay.GameMode.Timed"/> ("Classic") — see
    /// <see cref="MustyBlockBlast.Gameplay.Models.GameModeModel.SkinsEnabled"/>, which every conversion
    /// and draw path gates on. Every other mode never assigns anything but <see cref="None"/>.
    /// </para>
    /// <para>
    /// Stored per cell and copied by <see cref="Board.Clone"/>/<see cref="Board.CopyFrom"/>, so it is
    /// part of any board snapshot (undo). Member order matters for a future tie-break rule (sub-issue B:
    /// dominant-skin clear SFX, lowest ordinal wins ties) — append new themes, never reorder or rename
    /// existing ones.
    /// </para>
    /// </summary>
    public enum CellSkinKind
    {
        /// <summary>No decorative skin — the ordinary flat-colour block. Every cell before this feature
        /// existed, and every cell in a non-Classic mode, is this.</summary>
        None = 0,

        /// <summary>Cake-themed decorative overlay.</summary>
        Cake = 1,

        /// <summary>Candy-themed decorative overlay.</summary>
        Candy = 2,

        /// <summary>Jelly-themed decorative overlay.</summary>
        Jelly = 3,

        /// <summary>Fruit-themed decorative overlay.</summary>
        Fruit = 4,
    }
}
