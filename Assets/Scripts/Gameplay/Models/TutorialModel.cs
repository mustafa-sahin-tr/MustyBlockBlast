using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The one coach-mark on screen, if any, plus the ones still waiting their turn.
    /// <see cref="Systems.TutorialSystem"/> is the only writer; <see cref="Presentation.Views.TutorialOverlayView"/>
    /// observes <see cref="ActiveStep"/> to render (or hide) the spotlight.
    /// <para>
    /// The "seen" flags themselves live in <c>PlayerPrefs</c>, read and written by
    /// <see cref="Systems.TutorialSystem"/> alone (see <see cref="Systems.TutorialSeenKey"/>) — mirroring
    /// how <c>PowerUpModel</c>'s counts are the in-memory mirror of a persisted value that only its
    /// owning System touches directly. Duplicating that set here would be a second source of truth for
    /// no consumer that needs it: nothing outside <see cref="Systems.TutorialSystem"/> ever asks "has the
    /// player seen X" other than by way of a step never being queued for it.
    /// </para>
    /// </summary>
    public sealed class TutorialModel
    {
        /// <summary>The coach-mark currently showing, or null when none is. A struct wrapped in
        /// <see cref="System.Nullable{T}"/> rather than a sentinel value, so "no active step" can never
        /// be confused with a real one.</summary>
        public ReactiveProperty<TutorialStep?> ActiveStep { get; } = new ReactiveProperty<TutorialStep?>(null);

        /// <summary>Steps earned but not yet shown, oldest first. Plain <see cref="Queue{T}"/> rather
        /// than a <see cref="ReactiveProperty{T}"/>: nothing renders the queue itself, only
        /// <see cref="ActiveStep"/>, so nothing needs to observe it.</summary>
        internal Queue<TutorialStep> PendingSteps { get; } = new Queue<TutorialStep>();
    }
}
