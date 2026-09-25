using UnityEngine;

namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Authoring handle for a round charge badge (issue #449, generalised from #448's power-up button
    /// badge): a rim disc, a coloured disc and a <see cref="InfoDemoCounter"/> counting its charges
    /// down to 0. Returned by <see cref="InfoDemoHudChoreography.CountBadge"/> and ticked with
    /// <see cref="InfoDemoHudChoreography.TickBadge"/>. Build-time only.
    /// </summary>
    internal sealed class InfoDemoCountBadge
    {
        internal InfoDemoCountBadge(Vector2 centre, int rimId, int discId, int startCount, InfoDemoCounter counter)
        {
            Centre = centre;
            RimId = rimId;
            DiscId = discId;
            StartCount = startCount;
            Counter = counter;
        }

        /// <summary>The badge's centre in board units.</summary>
        internal Vector2 Centre { get; }

        internal int RimId { get; }

        internal int DiscId { get; }

        /// <summary>The count the badge shows at loop start.</summary>
        internal int StartCount { get; }

        /// <summary>The numbers, <see cref="StartCount"/> down to 0.</summary>
        internal InfoDemoCounter Counter { get; }

        /// <summary>The count showing once every tick queued so far has played.</summary>
        internal int Count => StartCount - Counter.Index;

        /// <summary>The label element showing <paramref name="count"/>.</summary>
        internal int LabelIdForCount(int count) => Counter.LabelId(Mathf.Clamp(StartCount - count, 0, Counter.ValueCount - 1));
    }
}
