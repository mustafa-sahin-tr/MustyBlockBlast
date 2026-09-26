using System;
using System.Collections.Generic;
using MustyBlockBlast.Core;
using MustyBlockBlast.Gameplay.Models;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// The family-objective piece guarantee (issue #513, docs/game-design.md "Drawing pieces"): on a
    /// level with an incomplete <see cref="ObjectiveType.PieceFamilyCount"/> objective, every
    /// <see cref="REFILL_WINDOW"/> normal tray refills contain at least one piece of that family. The
    /// ordinary draw has no idea what the level wants, and the rarest pieces (the squares) could
    /// otherwise go a dozen trays without showing up, stalling the one goal the level is about.
    /// <para>
    /// This class only keeps the count and answers "is family F owed a piece on this refill?".
    /// <c>BoardSystem</c> owns the dock and decides which slot the owed piece overwrites; it calls
    /// <see cref="BeginRun"/> as a run opens and <see cref="RecordRefill"/> after every normal refill
    /// has been dealt. Reroll and the no-moves rescue deliberately call neither — they are not refills
    /// in the sense of the rule, so they neither advance a window nor restart one.
    /// </para>
    /// <para>
    /// Counts are kept for every family, whether or not the level asks for it, and the objectives are
    /// consulted only when asking whether a family is due. That split is what makes the guarantee
    /// immune to a stale <see cref="ObjectiveProgress.IsComplete"/>: the run's opening refill happens
    /// inside <c>StartNewRun</c>, before <c>RunStartedMessage</c> has reset the previous run's progress,
    /// but it is the first refill of every window and so can never be due — nothing reads an objective
    /// until the second refill, by which time the reset has landed.
    /// </para>
    /// </summary>
    public sealed class FamilyPieceGuarantee
    {
        /// <summary>
        /// Normal refills per guarantee window: if the first <c>REFILL_WINDOW - 1</c> refills of a window
        /// held no piece of the family, the last one is forced to. Five refills is fifteen pieces. One
        /// global constant on purpose (issue #513) — not per-level tuning.
        /// </summary>
        public const int REFILL_WINDOW = 5;

        private readonly ObjectiveModel _objectiveModel;

        /// <summary>
        /// Consecutive normal refills that ended without a piece of each family, indexed by
        /// <c>(int)PieceFamily</c>. Zeroed by a refill that dealt one (the window restarts from it) and
        /// capped at <see cref="REFILL_WINDOW"/>, since anything past "due" means the same thing.
        /// </summary>
        private readonly int[] _refillsWithoutFamily = new int[PieceFamilyClassifier.FamilyCount];

        /// <summary>Scratch for <see cref="RecordRefill"/>: which families the refill just dealt. Kept
        /// rather than allocated per refill.</summary>
        private readonly bool[] _dealtThisRefill = new bool[PieceFamilyClassifier.FamilyCount];

        public FamilyPieceGuarantee(ObjectiveModel objectiveModel)
        {
            _objectiveModel = objectiveModel ?? throw new ArgumentNullException(nameof(objectiveModel));
        }

        /// <summary>Opens a fresh window for every family. Called by <c>BoardSystem.StartNewRun</c>
        /// before its opening refill, so no count survives a game over, a retry or a level change.</summary>
        public void BeginRun()
        {
            Array.Clear(_refillsWithoutFamily, 0, _refillsWithoutFamily.Length);
        }

        /// <summary>
        /// True when the refill about to be recorded must contain a piece of <paramref name="family"/>:
        /// the level still wants that family and the window's earlier refills all went without it.
        /// Stays true across refills until a piece of the family is actually recorded, so a guarantee
        /// that could not be given a slot is simply paid on the next refill.
        /// </summary>
        public bool IsDue(PieceFamily family)
        {
            return _refillsWithoutFamily[(int)family] >= REFILL_WINDOW - 1 && IsWanted(family);
        }

        /// <summary>
        /// Records one finished normal refill from the dock as it was finally dealt — ordinary draw,
        /// special injections and forced pieces alike, since each is a piece the player really has.
        /// A family on the dock restarts its window from this refill; every other family moves one
        /// refill closer to being due.
        /// </summary>
        public void RecordRefill(TrayModel trayModel)
        {
            Array.Clear(_dealtThisRefill, 0, _dealtThisRefill.Length);

            for (int slotIndex = 0; slotIndex < TrayModel.SLOT_COUNT; slotIndex++)
            {
                Piece piece = trayModel.GetPiece(slotIndex);
                if (piece != null)
                {
                    _dealtThisRefill[(int)PieceFamilyClassifier.Classify(piece.Id)] = true;
                }
            }

            for (int familyIndex = 0; familyIndex < _refillsWithoutFamily.Length; familyIndex++)
            {
                if (_dealtThisRefill[familyIndex])
                {
                    _refillsWithoutFamily[familyIndex] = 0;
                }
                else if (_refillsWithoutFamily[familyIndex] < REFILL_WINDOW)
                {
                    _refillsWithoutFamily[familyIndex]++;
                }
            }
        }

        /// <summary>Consecutive recorded refills without <paramref name="family"/>. Test seam only.</summary>
        internal int RefillsWithout(PieceFamily family) => _refillsWithoutFamily[(int)family];

        /// <summary>
        /// Whether any tracked objective is an incomplete <see cref="ObjectiveType.PieceFamilyCount"/>
        /// for <paramref name="family"/>. Two objectives for the same family share one window — the
        /// guarantee is about the family appearing, and one piece serves both — and the family stops
        /// being wanted only once every one of them is complete.
        /// </summary>
        private bool IsWanted(PieceFamily family)
        {
            IReadOnlyList<ObjectiveProgress> objectives = _objectiveModel.TrackedObjectives;
            for (int objectiveIndex = 0; objectiveIndex < objectives.Count; objectiveIndex++)
            {
                ObjectiveProgress progress = objectives[objectiveIndex];
                if (progress.Definition.Type == ObjectiveType.PieceFamilyCount
                    && progress.Definition.RequiredPieceFamily == family
                    && !progress.IsComplete)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
