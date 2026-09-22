using MustyBlockBlast.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views.Shared
{
    /// <summary>One tappable mode plate: the mode it selects plus the bits that repaint on selection.
    /// Built by <see cref="ModePlateBuilder"/>; hit-tested by whichever View hosts it.</summary>
    internal sealed class ModeOption
    {
        internal ModeOption(GameMode mode, RectTransform rect, PlateSelection selection, Text playingText)
        {
            Mode = mode;
            Rect = rect;
            Selection = selection;
            PlayingText = playingText;
        }

        internal GameMode Mode { get; }

        internal RectTransform Rect { get; }

        internal PlateSelection Selection { get; }

        /// <summary>The PLAYING tag: accent while this is the mode being played, clear otherwise.</summary>
        internal Text PlayingText { get; }
    }
}
