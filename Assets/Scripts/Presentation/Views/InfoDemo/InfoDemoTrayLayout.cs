namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>How the tray strip under the mini board is divided (see
    /// <see cref="InfoDemoLayout.TraySlot(int, InfoDemoTrayLayout)"/>).</summary>
    internal enum InfoDemoTrayLayout
    {
        /// <summary>Three equal slots across the whole strip — the plain tray.</summary>
        ThreeSlots,

        /// <summary>A progress chip in the strip's left zone (<see cref="InfoDemoLayout.ChipCentre"/>)
        /// and the three tray slots packed into the right zone — every objective demo (#447–#454).</summary>
        ChipLeft,
    }
}
