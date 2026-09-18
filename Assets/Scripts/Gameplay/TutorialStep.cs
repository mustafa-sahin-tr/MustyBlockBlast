using MustyBlockBlast.Core;

namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// One coach-mark: a target to spotlight, an arrow to point at it, and the body copy to show
    /// beside it. Pure data — built and owned by <see cref="Systems.TutorialSystem"/>, read by
    /// <see cref="Models.TutorialModel"/> and rendered by
    /// <see cref="Presentation.Views.TutorialOverlayView"/> — so it carries no Unity types.
    /// </summary>
    public readonly struct TutorialStep
    {
        public TutorialStep(
            string id,
            TutorialTargetId targetId,
            string bodyLocalizationKey,
            TutorialArrowDirection arrowDirection,
            GridPosition? boardPosition = null,
            int slotIndex = -1)
        {
            Id = id;
            TargetId = targetId;
            BodyLocalizationKey = bodyLocalizationKey;
            ArrowDirection = arrowDirection;
            BoardPosition = boardPosition;
            SlotIndex = slotIndex;
        }

        /// <summary>Stable identity for this step, unique per trigger instance (e.g. one per
        /// <see cref="PowerUpKind"/> unlocked, one per <see cref="Core.SpecialCellKind"/> spawned).
        /// Doubles as the persistence key suffix for the "seen" flag — see
        /// <see cref="Systems.TutorialSeenKey"/> — and as the identity <see cref="Systems.TutorialSystem.NotifyTargetInteracted"/>
        /// compares against.</summary>
        public string Id { get; }

        /// <summary>Which kind of element to spotlight. Resolved to an actual on-screen rect by
        /// <see cref="Presentation.Views.TutorialOverlayView"/>.</summary>
        public TutorialTargetId TargetId { get; }

        /// <summary>String Table key (see <see cref="Localization.LocalizationKeys"/>) for the coach-mark's
        /// body copy.</summary>
        public string BodyLocalizationKey { get; }

        /// <summary>Which side the pointer arrow points from, or <see cref="TutorialArrowDirection.None"/>
        /// for none.</summary>
        public TutorialArrowDirection ArrowDirection { get; }

        /// <summary>The board cell this step targets, when <see cref="TargetId"/> is
        /// <see cref="TutorialTargetId.BoardCell"/>. Null otherwise.</summary>
        public GridPosition? BoardPosition { get; }

        /// <summary>The dock or power-up strip slot this step targets, when <see cref="TargetId"/> is
        /// <see cref="TutorialTargetId.PowerUpStripSlot"/> or <see cref="TutorialTargetId.TraySlot"/>.
        /// <c>-1</c> otherwise.</summary>
        public int SlotIndex { get; }

        /// <summary>Whether <paramref name="targetId"/> (with its payload) is exactly the element this
        /// step spotlights. The one comparison both <see cref="Presentation.Views.BoardInputView"/>'s
        /// input guard and <see cref="Systems.TutorialSystem.NotifyTargetInteracted"/> need, kept in one
        /// place so they can never disagree about what counts as "the target".</summary>
        public bool Matches(TutorialTargetId targetId, GridPosition? boardPosition, int slotIndex)
        {
            if (targetId != TargetId)
            {
                return false;
            }

            switch (targetId)
            {
                case TutorialTargetId.BoardCell:
                    return boardPosition != null && BoardPosition != null
                        && boardPosition.Value.Equals(BoardPosition.Value);
                case TutorialTargetId.PowerUpStripSlot:
                case TutorialTargetId.TraySlot:
                    return slotIndex == SlotIndex;
                default:
                    // HoldSlot (and any future target with no payload): the kind alone is the identity.
                    return true;
            }
        }
    }
}
