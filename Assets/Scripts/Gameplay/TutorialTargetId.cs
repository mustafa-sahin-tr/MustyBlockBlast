namespace MustyBlockBlast.Gameplay
{
    /// <summary>
    /// The on-screen element a <see cref="TutorialStep"/> spotlights. Deliberately not a
    /// <c>RectTransform</c> or any other Unity type — <see cref="Models.TutorialModel"/> and
    /// <see cref="Systems.TutorialSystem"/> are pure C# and must stay that way — so a step only ever
    /// says <em>which kind</em> of element it targets, plus whatever payload
    /// (<see cref="TutorialStep.BoardPosition"/> / <see cref="TutorialStep.SlotIndex"/>) narrows that
    /// down to one instance. Resolving an id to an actual <c>RectTransform</c> is
    /// <see cref="Presentation.Views.TutorialOverlayView"/>'s job.
    /// </summary>
    public enum TutorialTargetId
    {
        /// <summary>A slot in the power-up strip (see <see cref="TutorialStep.SlotIndex"/> for which
        /// <see cref="PowerUpKind"/>'s slot).</summary>
        PowerUpStripSlot,

        /// <summary>A single board cell (see <see cref="TutorialStep.BoardPosition"/>).</summary>
        BoardCell,

        /// <summary>The Hold slot ("pocket") beside the dock. Carries no payload — there is only one.</summary>
        HoldSlot,

        /// <summary>A dock slot holding a special piece (see <see cref="TutorialStep.SlotIndex"/>).</summary>
        TraySlot,
    }
}
