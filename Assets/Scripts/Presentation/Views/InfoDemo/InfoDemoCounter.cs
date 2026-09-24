namespace MustyBlockBlast.Presentation.Views
{
    /// <summary>
    /// Authoring handle for a demo counter (issue #449) — a run of literal labels stacked on one spot,
    /// one per value it will show, of which exactly one is visible at a time: a charge badge counting
    /// 3 → 2 → 1 → 0, a wallet going "120" → "125". Returned by
    /// <see cref="InfoDemoHudChoreography.Counter"/> and advanced with
    /// <see cref="InfoDemoHudChoreography.AdvanceCounter"/>. The position it tracks only moves while the
    /// timeline is being built; nothing here is touched at play time.
    /// </summary>
    internal sealed class InfoDemoCounter
    {
        private readonly int[] _labelIds;

        internal InfoDemoCounter(int[] labelIds)
        {
            _labelIds = labelIds;
        }

        /// <summary>How many values the counter can show.</summary>
        internal int ValueCount => _labelIds.Length;

        /// <summary>Index of the value showing once every advance queued so far has played.</summary>
        internal int Index { get; private set; }

        internal bool IsAtEnd => Index >= _labelIds.Length - 1;

        /// <summary>The label element of value <paramref name="valueIndex"/> (0 = the one showing at loop start).</summary>
        internal int LabelId(int valueIndex) => _labelIds[valueIndex];

        internal void Advance()
        {
            if (!IsAtEnd)
            {
                Index++;
            }
        }
    }
}
