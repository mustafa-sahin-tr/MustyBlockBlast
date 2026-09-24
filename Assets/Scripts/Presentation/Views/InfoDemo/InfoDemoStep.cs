using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// One tween in an <see cref="InfoDemoTimeline"/>: drive <see cref="Property"/> of element
    /// <see cref="ElementId"/> from <see cref="From"/> to <see cref="To"/> over
    /// [<see cref="StartTime"/>, <see cref="StartTime"/> + <see cref="Duration"/>] through
    /// <see cref="Easing"/>. Once started a step holds its end value until a later step on the same
    /// property takes over, so a timeline reads as "what happens when" rather than as keyframes.
    /// A value type in a flat array, so evaluating a frame walks memory and allocates nothing.
    /// </summary>
    internal readonly struct InfoDemoStep
    {
        internal readonly int ElementId;
        internal readonly InfoDemoProperty Property;
        internal readonly float StartTime;
        internal readonly float Duration;
        internal readonly Vector4 From;
        internal readonly Vector4 To;
        internal readonly InfoDemoEasing Easing;

        internal InfoDemoStep(
            int elementId,
            InfoDemoProperty property,
            float startTime,
            float duration,
            Vector4 from,
            Vector4 to,
            InfoDemoEasing easing)
        {
            ElementId = elementId;
            Property = property;
            StartTime = startTime;
            Duration = duration;
            From = from;
            To = to;
            Easing = easing;
        }
    }
}
