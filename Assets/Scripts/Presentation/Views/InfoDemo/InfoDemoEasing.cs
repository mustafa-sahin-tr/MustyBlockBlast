namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>Easing curve an <see cref="InfoDemoStep"/> is sampled through — see
    /// <see cref="InfoDemoEase.Evaluate"/>.</summary>
    internal enum InfoDemoEasing
    {
        Linear,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,

        /// <summary>Overshoots slightly past the target and settles — the "pop" a landing cell makes.</summary>
        EaseOutBack,

        /// <summary>Goes from <c>From</c> to <c>To</c> and back to <c>From</c> over the step (a half
        /// sine) — a single pulse without needing a second step to return.</summary>
        Pulse,
    }
}
