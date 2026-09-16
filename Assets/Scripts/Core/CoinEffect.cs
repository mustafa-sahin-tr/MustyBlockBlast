using System;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// <see cref="SpecialCellKind.Coin"/>'s "effect": totalling a payout, and nothing else. A coin cell
    /// clears no area and refills none, so this deliberately never touches the board — it exists because
    /// the cascade loop (<see cref="CascadeClearResolver"/>) keeps the triggers it collects to itself,
    /// and <see cref="ISpecialCellEffect"/> is the one seam through which a caller can learn that a coin
    /// was among the cells a resolution destroyed. Shaped exactly like <see cref="ScoreGemEffect"/>, the
    /// other kind whose whole consequence is off the board.
    /// <para>
    /// Because it mutates nothing it can never chain: a resolution that destroys only coins settles on
    /// the very next iteration, the same as one that destroyed no special cell at all.
    /// </para>
    /// <para>
    /// A coin destroyed at <see cref="ClearAxis.Both"/> — it sat on the intersection of a row and a
    /// column closed in the same destruction — pays double. That reads the very same axis signal
    /// <see cref="LaserEffect"/> already reads, recorded by whatever destroyed the cell at the one moment
    /// it was still known; re-deriving it here would be impossible, because by now the cell is empty and
    /// the board no longer knows what removed it. Every other axis, <see cref="ClearAxis.None"/> included
    /// (a Bomb, a Colour Cleanser), pays the base amount: those are single destructions, however they
    /// came about.
    /// </para>
    /// <para>
    /// The per-cell value is supplied by whichever System constructs this rather than being a constant
    /// here, because it is an economy number and the economy is configured in a Gameplay-layer asset that
    /// Core must never reference. Core owns "a coin pays, and pays twice on an intersection"; the
    /// Gameplay layer owns how much.
    /// </para>
    /// <para>
    /// Long-lived by design, exactly as <see cref="ExplosiveCoreEffect"/> and <see cref="LaserEffect"/>
    /// are: one instance per System that resolves clears, with <see cref="BeginResolution"/> called
    /// before each resolution rather than a fresh instance allocated. <see cref="TotalCoinsAwarded"/> is
    /// therefore a reading this instance overwrites — a caller that needs it beyond the current
    /// resolution must copy it.
    /// </para>
    /// </summary>
    public sealed class CoinEffect : ISpecialCellEffect
    {
        /// <summary>How many coins one destroyed coin cell is worth before the intersection doubling.</summary>
        private readonly int _coinValuePerCell;

        public CoinEffect(int coinValuePerCell)
        {
            _coinValuePerCell = coinValuePerCell;
        }

        /// <summary>Coins owed for every coin cell destroyed since the last
        /// <see cref="BeginResolution"/>, doubling already applied.</summary>
        public int TotalCoinsAwarded { get; private set; }

        /// <summary>Starts a new resolution: forgets the previous one's total. Must be called before the
        /// resolution that will apply this effect, or two resolutions' coins would be reported as one —
        /// and the player would be paid a second time for coins the first resolution already banked.</summary>
        public void BeginResolution() => TotalCoinsAwarded = 0;

        /// <summary>Adds <paramref name="trigger"/>'s payout when it is a coin, and does nothing else to
        /// anything. <paramref name="board"/> is validated but never read or written.</summary>
        public void Apply(Board board, SpecialCellTrigger trigger)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            if (trigger.Kind != SpecialCellKind.Coin)
            {
                return;
            }

            TotalCoinsAwarded += trigger.Axis == ClearAxis.Both
                ? _coinValuePerCell * 2
                : _coinValuePerCell;
        }
    }
}
