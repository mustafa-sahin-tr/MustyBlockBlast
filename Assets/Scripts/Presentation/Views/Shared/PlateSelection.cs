using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views.Shared
{
    /// <summary>The parts of a choosable plate that repaint on selection: the ring, its gap and,
    /// on the plates that have one, the check disc. Shared by every plate picker — the settings card's
    /// theme, language, mode and duration plates and the mode-select screen's (issue #379).</summary>
    internal sealed class PlateSelection
    {
        internal PlateSelection(Image ring, Image gap)
        {
            Ring = ring;
            Gap = gap;
        }

        internal Image Ring { get; }

        internal Image Gap { get; }

        internal Image CheckLip { get; set; }

        internal Image CheckDisc { get; set; }

        internal Image CheckGlyph { get; set; }
    }
}
