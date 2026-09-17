using System;
using System.Collections.Generic;

namespace MustyBlockBlast.Core
{
    /// <summary>
    /// Applies one destroyed special cell's effect to the board. The extension seam every real
    /// <see cref="SpecialCellKind"/> will plug into: <see cref="CascadeClearResolver"/> owns the
    /// "clear, detect, apply, re-check" sequencing and knows nothing about what any individual kind
    /// does.
    /// <para>
    /// An implementation may occupy cells, clear cells, or do nothing. It must not clear whole lines
    /// on the grounds that they are full — the loop re-checks fullness itself on the next iteration,
    /// so clearing a completed line from inside an effect would double-report it.
    /// </para>
    /// </summary>
    public interface ISpecialCellEffect
    {
        /// <summary>Applies <paramref name="trigger"/>'s effect. Called after the cells of the phase
        /// that destroyed it have already been emptied, so the board is in its post-clear state and
        /// the trigger's own cell is already empty.</summary>
        void Apply(Board board, SpecialCellTrigger trigger);
    }

    /// <summary>
    /// Everything one placement's resolution did, phase by phase.
    /// <para>
    /// <b>The reporting contract, which later sub-issues inherit:</b> <see cref="Primary"/> — phase 0,
    /// the clear the placement itself caused — is what the existing placement messages, scoring and
    /// objectives read, and it is byte-for-byte what the old single-pass resolver returned. Clears
    /// caused by a special cell's effect are separate phases and are reported separately rather than
    /// summed into phase 0. Two reasons: a secondary clear is not something the player lined up, so
    /// folding it into "lines cleared by this placement" would silently inflate combo streaks and
    /// every line-count objective; and keeping them apart means the reward policy for cascades (score
    /// them at all? at a different rate? as their own streak?) is still an open, changeable decision
    /// when the first real effect ships, instead of being locked in by a summation nobody reviewed.
    /// </para>
    /// </summary>
    public readonly struct CascadeClearResult
    {
        public CascadeClearResult(IReadOnlyList<LineClearResult> phases, bool stoppedAtIterationCap)
        {
            if (phases == null)
            {
                throw new ArgumentNullException(nameof(phases));
            }

            if (phases.Count == 0)
            {
                throw new ArgumentException("A resolution always has a phase 0.", nameof(phases));
            }

            Phases = phases;
            StoppedAtIterationCap = stoppedAtIterationCap;
        }

        /// <summary>Every clear phase in the order it resolved. Always holds at least phase 0, which
        /// reports no lines when the placement completed nothing.</summary>
        public IReadOnlyList<LineClearResult> Phases { get; }

        /// <summary>True when the loop stopped because it reached
        /// <see cref="CascadeClearResolver.MAX_CASCADE_ITERATIONS"/> rather than because the board
        /// settled. Surfaced for tests and diagnostics; the board is left in a consistent state either
        /// way.</summary>
        public bool StoppedAtIterationCap { get; }

        /// <summary>The clear the placement itself caused — the only phase existing scoring,
        /// objectives and messages read. See the type's remarks for why.</summary>
        public LineClearResult Primary => Phases[0];

        /// <summary>Phases beyond phase 0, i.e. clears a special cell's effect caused. Always 0 while
        /// every cell is <see cref="SpecialCellKind.None"/>.</summary>
        public int CascadePhaseCount => Phases.Count - 1;

        public bool AnyCascaded => CascadePhaseCount > 0;

        /// <summary>Lines cleared across every phase, primary and cascaded alike. A sum over a handful
        /// of phases, computed on demand rather than cached, because it is read at most once per
        /// placement and never in a per-frame path.</summary>
        public int TotalLineCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Phases.Count; i++)
                {
                    total += Phases[i].LineCount;
                }

                return total;
            }
        }

        /// <summary>
        /// Reinforced cells finished off across every phase, primary and cascaded alike.
        /// <para>
        /// Data plumbing for issue #154's "clear all reinforced cells" objective — the whole
        /// resolution, not just the primary phase, because a reinforced cell is destroyed by whatever
        /// destroyed it, exactly as <c>PiecePlacedMessage.DestroyedScoreGemCount</c> is read off the
        /// whole resolution. Cells finished off by a special cell's own blast/wipe/strike are
        /// <em>not</em> included: those never pass through a clear phase, and whether they should count
        /// is a scoring decision for #154 to make with its objective in hand.
        /// </para>
        /// </summary>
        public int TotalReinforcedCellsFullyClearedCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Phases.Count; i++)
                {
                    total += Phases[i].ReinforcedCellsFullyClearedCount;
                }

                return total;
            }
        }

        /// <summary>Cells emptied across every phase. Phases never overlap — a cell emptied in one
        /// phase has to be refilled by an effect before another phase can clear it again — so this is
        /// a true total, not a distinct-cell count.</summary>
        public int TotalClearedCellCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Phases.Count; i++)
                {
                    total += Phases[i].ClearedCellCount;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// Resolves a placement's clears to completion: clear the full lines, detect the special cells
    /// that went with them, apply their effects, then look again for lines those effects completed,
    /// and repeat until the board settles.
    /// <para>
    /// Sits on top of <see cref="LineClearResolver.ResolveClears(Board)"/> rather than replacing it —
    /// each iteration <em>is</em> one ordinary single-pass clear, so a placement with no special cells
    /// involved runs exactly one iteration and produces exactly the result the single-pass resolver
    /// always did. Preview paths keep calling the single-pass resolver directly: chaining is a
    /// property of a placement actually happening, never of a drag hint.
    /// </para>
    /// <para>
    /// Fully synchronous. It resolves state only; spacing the phases out visually is Presentation's
    /// job, and <see cref="CascadeClearResult.Phases"/> is the ordered list it will replay.
    /// </para>
    /// </summary>
    public static class CascadeClearResolver
    {
        /// <summary>
        /// How many clear phases one placement may resolve before the loop stops. A chain only
        /// continues while an effect refills cells fast enough to complete yet another line, so a
        /// designed chain is a handful of phases at most — no real board can sustain twenty, and this
        /// is set well clear of anything a special cell is meant to do rather than at the edge of it.
        /// <para>
        /// It exists because the loop is driven by effect implementations that do not exist yet: a
        /// future effect that refills the line that triggered it would otherwise spin forever on the
        /// main thread and hang the game. Reaching the cap is not a failure state — the board is
        /// left consistent, whatever is still full is simply left standing, and the next placement's
        /// phase 0 clears it, since phase 0 always clears "whatever is full right now".
        /// </para>
        /// </summary>
        public const int MAX_CASCADE_ITERATIONS = 20;

        /// <summary>Resolves with no special-cell effects installed. Every kind is
        /// <see cref="SpecialCellKind.None"/> today, so this is what the game uses: a single clear
        /// phase, identical to <see cref="LineClearResolver.ResolveClears(Board)"/>.</summary>
        public static CascadeClearResult ResolveCascade(Board board) => ResolveCascade(board, null);

        /// <summary>Resolves, applying <paramref name="effect"/> to each destroyed special cell. A
        /// null <paramref name="effect"/> means nothing can chain, so the loop stops after phase 0.</summary>
        public static CascadeClearResult ResolveCascade(Board board, ISpecialCellEffect effect)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }

            // Sized for the common case of exactly one phase. The per-phase row/column lists that
            // LineClearResolver returns escape into here, so they cannot be pooled; this runs once per
            // placement, not per frame, and is not held to the Update-path zero-alloc rule.
            var phases = new List<LineClearResult>(1);
            var destroyedCells = new List<GridPosition>(Board.SIZE * 2);
            var triggeredSpecials = new List<SpecialCellTrigger>();
            bool stoppedAtIterationCap = false;

            for (int iteration = 0; ; iteration++)
            {
                if (iteration >= MAX_CASCADE_ITERATIONS)
                {
                    stoppedAtIterationCap = true;
                    break;
                }

                LineClearResult phase = LineClearResolver.ResolveClears(
                    board, destroyedCells, triggeredSpecials);

                // Phase 0 is recorded even when it cleared nothing, so Primary always exists and a
                // placement that completed no line reports exactly the empty result it used to. Later
                // iterations only exist because something cleared, so an empty one ends the chain
                // instead of padding the list.
                if (iteration == 0 || phase.AnyCleared)
                {
                    phases.Add(phase);
                }

                if (!phase.AnyCleared || triggeredSpecials.Count == 0 || effect == null)
                {
                    break;
                }

                for (int i = 0; i < triggeredSpecials.Count; i++)
                {
                    effect.Apply(board, triggeredSpecials[i]);
                }
            }

            return new CascadeClearResult(phases, stoppedAtIterationCap);
        }
    }
}
