namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// What a tap on the end-of-run card asked for (issue #266). <see cref="RunResultView"/> resolves
    /// the tap to one of these and <see cref="BoardInputView"/> carries it out, so the card stays a
    /// View that knows which button was hit and nothing about what a restart or a mode switch is.
    /// A badge claim is not listed: the card settles that itself and reports <see cref="None"/>.
    /// </summary>
    internal enum RunEndAction
    {
        /// <summary>The tap landed on nothing actionable — the scrim, a label, a claimed badge.</summary>
        None,

        /// <summary>"Play again" / "Try again": restart the same mode (and, in Path, the same level).</summary>
        PlayAgain,

        /// <summary>"Change mode": open the hub on the settings tab, where the mode row lives.</summary>
        ChangeMode,

        /// <summary>"Next level · n": start the level after the one just completed.</summary>
        NextLevel,

        /// <summary>"Watch ad" (issue #371): take back a no-moves ending through
        /// <c>BoardSystem.TryApplyNoMovesRescueAsync</c> — a rewarded ad for a fresh dock, not a restart.</summary>
        WatchAd,
    }
}
