using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The move budget of a Path level whose objective is "score within N moves" (issue #465). Owned and
    /// written by <see cref="Systems.MoveBudgetSystem"/>; <see cref="Systems.BoardSystem"/> reads it to
    /// refuse placements once it is spent, and the HUD counter and the out-of-moves sheet read it.
    /// <para>
    /// Kept as moves <em>used</em> plus moves <em>granted</em> rather than a single count left, so a
    /// snapshot of the used count alone rewinds a placement without taking back moves an ad already
    /// paid for — the shape one-step Undo needs when it lands (the issue's AC6).
    /// </para>
    /// </summary>
    public sealed class MoveBudgetModel
    {
        /// <summary>Whether this run has a budget at all: a Path run on a level with a ScoreInMoves
        /// objective. False everywhere else, and then nothing below means anything.</summary>
        public ReactiveProperty<bool> IsActive { get; } = new ReactiveProperty<bool>(false);

        /// <summary>Moves still available: the level's limit plus any granted, minus those used. Never
        /// negative.</summary>
        public ReactiveProperty<int> MovesLeft { get; } = new ReactiveProperty<int>(0);

        /// <summary>True while the budget is spent short of the target and the "+moves" offer waits on
        /// the player. The run is held — no placement, no game-over check — until it is answered.</summary>
        public ReactiveProperty<bool> IsOfferOpen { get; } = new ReactiveProperty<bool>(false);

        /// <summary>The level's authored budget for this run.</summary>
        public int MoveLimit { get; internal set; }

        /// <summary>Board placements made this run.</summary>
        public int MovesUsed { get; internal set; }

        /// <summary>Moves the rewarded offer added this run (0 or the config's amount).</summary>
        public int ExtraMoves { get; internal set; }

        /// <summary>Whether this level attempt already had its one offer — taken, declined or failed.</summary>
        public bool IsOfferSpent { get; internal set; }

        /// <summary>Moves left over when the target was reached, and the points they paid — kept for the
        /// result card's bonus row. Both 0 until a level with a budget is cleared.</summary>
        public int LeftoverMoves { get; internal set; }

        public int LeftoverBonus { get; internal set; }
    }
}
