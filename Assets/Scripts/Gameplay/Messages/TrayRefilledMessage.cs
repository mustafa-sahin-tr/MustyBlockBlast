namespace MustyBlockBlast.Gameplay.Messages
{
    /// <summary>
    /// A fresh tray of three pieces was drawn — both the opening draw of a run and every mid-run
    /// refill. The timed-mode countdown resets on this and only on this, which is what makes the
    /// clock a "clear the tray in time" pressure rather than a per-placement one.
    /// </summary>
    public readonly struct TrayRefilledMessage
    {
    }
}
