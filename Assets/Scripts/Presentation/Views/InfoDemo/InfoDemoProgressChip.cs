namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Authoring handle for an objective demo's progress chip (issue #447), returned by
    /// <see cref="InfoDemoChoreography.ProgressChip"/>: the element ids of the chip's parts and the
    /// counter value the script has advanced it to so far. Used only while a timeline is being built —
    /// <see cref="InfoDemoChoreography.AdvanceChip"/> reads and moves <see cref="Value"/> — and never
    /// touched at play time.
    /// </summary>
    internal sealed class InfoDemoProgressChip
    {
        private readonly int[] _counterLabelIds;
        private readonly int _startValue;

        internal InfoDemoProgressChip(
            int panelId, int captionId, int[] counterLabelIds, int startValue, int target, int badgeId, int checkId)
        {
            PanelId = panelId;
            CaptionId = captionId;
            _counterLabelIds = counterLabelIds;
            _startValue = startValue;
            Target = target;
            Value = startValue;
            BadgeId = badgeId;
            CheckId = checkId;
        }

        /// <summary>The white chip itself.</summary>
        internal int PanelId { get; }

        /// <summary>The caption on the chip's top line (e.g. "EXACTLY 2").</summary>
        internal int CaptionId { get; }

        /// <summary>The green disc of the "done" badge at the chip's top-right.</summary>
        internal int BadgeId { get; }

        /// <summary>The tick on the badge.</summary>
        internal int CheckId { get; }

        internal int Target { get; }

        /// <summary>The counter value showing after every <see cref="InfoDemoChoreography.AdvanceChip"/>
        /// queued so far.</summary>
        internal int Value { get; private set; }

        internal bool IsComplete => Value >= Target;

        /// <summary>Id of the counter label reading "<paramref name="value"/>/<see cref="Target"/>".</summary>
        internal int CounterLabelId(int value) => _counterLabelIds[value - _startValue];

        internal void Advance() => Value++;
    }
}
