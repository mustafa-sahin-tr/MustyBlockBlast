using UnityEngine;
using UnityEngine.UI;

namespace MustyBlockBlast.Presentation.Views.Shared
{
    /// <summary>One tappable duration chip on the Timed plate: the length it selects plus its
    /// selection visuals. Built by <see cref="ModePlateBuilder"/>; hit-tested by whichever View hosts it.</summary>
    internal sealed class DurationOption
    {
        internal DurationOption(float seconds, RectTransform rect, PlateSelection selection, Text nameText)
        {
            Seconds = seconds;
            Rect = rect;
            Selection = selection;
            NameText = nameText;
        }

        internal float Seconds { get; }

        internal RectTransform Rect { get; }

        internal PlateSelection Selection { get; }

        internal Text NameText { get; }
    }
}
