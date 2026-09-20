using System;
using MustyBlockBlast.Core;
using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>What the Ghost Fit hint is currently saying.</summary>
    public enum GhostFitHintState
    {
        /// <summary>Nothing on screen. The resting state, and where every dismissal lands.</summary>
        None,

        /// <summary>A concrete move is being suggested; <see cref="GhostFitHint.Move"/> is the move.</summary>
        Suggested,

        /// <summary>The search found no legal placement for any dock piece. Nothing is suggested and
        /// nothing was spent — the player is simply told there is no move to point at.</summary>
        NoPlacements,
    }

    /// <summary>
    /// The hint as one value: its state and, when there is one, the move it points at. Kept as a single
    /// struct rather than a state flag beside a move so the two can never be read out of step — a
    /// subscriber woken by a state change always sees the move that belongs to it.
    /// </summary>
    public readonly struct GhostFitHint : IEquatable<GhostFitHint>
    {
        private GhostFitHint(GhostFitHintState state, GhostFitMove move)
        {
            State = state;
            Move = move;
        }

        /// <summary>The resting value: nothing suggested, nothing reported.</summary>
        public static GhostFitHint None => default;

        public GhostFitHintState State { get; }

        /// <summary>Meaningful only while <see cref="State"/> is
        /// <see cref="GhostFitHintState.Suggested"/>; default otherwise.</summary>
        public GhostFitMove Move { get; }

        public static GhostFitHint ForMove(GhostFitMove move)
            => new GhostFitHint(GhostFitHintState.Suggested, move);

        public static GhostFitHint NoPlacements()
            => new GhostFitHint(GhostFitHintState.NoPlacements, default);

        public bool Equals(GhostFitHint other) => State == other.State && Move.Equals(other.Move);

        public override bool Equals(object obj) => obj is GhostFitHint other && Equals(other);

        public override int GetHashCode() => unchecked(((int)State * 397) ^ Move.GetHashCode());
    }

    /// <summary>
    /// The live Ghost Fit suggestion. Mutated exclusively by <c>GhostFitSystem</c>; the board, tray and
    /// HUD views only read it.
    /// <para>
    /// Run state, never persisted — like the armed selection and the 2x window, and unlike the inventory
    /// count that paid for it. Unlike every other power-up's effect it is not a mutation at all: nothing
    /// on the board or in the tray changes, so this model <em>is</em> the whole effect.
    /// </para>
    /// </summary>
    public sealed class GhostFitModel
    {
        public ReactiveProperty<GhostFitHint> Hint { get; } =
            new ReactiveProperty<GhostFitHint>(GhostFitHint.None);

        /// <summary>True while a concrete move is being suggested.</summary>
        public bool IsSuggesting => Hint.Value.State == GhostFitHintState.Suggested;

        /// <summary>The dock slot the suggestion points at, or -1 when nothing is suggested.</summary>
        public int SuggestedSlotIndex => IsSuggesting ? Hint.Value.Move.SlotIndex : -1;
    }
}
