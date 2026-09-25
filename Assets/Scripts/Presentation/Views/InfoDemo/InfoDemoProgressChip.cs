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

        /// <summary>The value each of <see cref="_counterLabelIds"/> reads, ascending — every whole number from
        /// the start to the target for a counting chip, or just the readings a score chip jumps between
        /// (issue #454), so a 2820 → 3000 chip holds two labels rather than a hundred and eighty-one.</summary>
        private readonly int[] _values;

        internal InfoDemoProgressChip(
            int panelId, int captionId, int[] counterLabelIds, int[] values, int target, int badgeId, int checkId)
        {
            PanelId = panelId;
            CaptionId = captionId;
            _counterLabelIds = counterLabelIds;
            _values = values;
            Target = target;
            Value = values[0];
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

        /// <summary>Id of the counter label reading "<paramref name="value"/>/<see cref="Target"/>". Throws for
        /// a value the chip was not built with — an authoring mistake, caught while the timeline is built.</summary>
        internal int CounterLabelId(int value)
        {
            int valueIndex = System.Array.IndexOf(_values, value);
            if (valueIndex < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(value), value, "The chip has no label for this value.");
            }

            return _counterLabelIds[valueIndex];
        }

        /// <summary>Whether the chip was built with a label reading <paramref name="value"/>.</summary>
        internal bool HasValue(int value) => System.Array.IndexOf(_values, value) >= 0;

        internal void Advance() => Advance(1);

        /// <summary>Moves <see cref="Value"/> on by <paramref name="steps"/> at once (issue #453 — one clear
        /// that destroys several counted things), never past <see cref="Target"/>.</summary>
        internal void Advance(int steps) => Value = System.Math.Min(Target, Value + System.Math.Max(0, steps));

        /// <summary>Moves <see cref="Value"/> straight to <paramref name="value"/> (issue #454 — a score chip
        /// jumping by a whole placement's points).</summary>
        internal void AdvanceTo(int value) => Value = value;
    }
}
