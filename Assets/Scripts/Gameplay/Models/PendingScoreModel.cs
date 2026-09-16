using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// Scores earned while the backend was out of reach, in the order they were earned. The queue is
    /// the run's receipt: an entry stays here until the backend has confirmed it, so a failed flush
    /// costs a retry rather than the score.
    /// <para>
    /// Order is preserved but carries no meaning for the backend — the boards are "Keep Best", so the
    /// order a backlog drains in cannot change what ends up ranked. It is kept only so the oldest score
    /// is retried first.
    /// </para>
    /// </summary>
    public sealed class PendingScoreModel
    {
        private readonly List<PendingScoreEntry> _entries = new List<PendingScoreEntry>();

        public IReadOnlyList<PendingScoreEntry> Entries => _entries;

        internal void Add(PendingScoreEntry entry) => _entries.Add(entry);

        internal void RemoveAt(int index) => _entries.RemoveAt(index);

        /// <summary>
        /// Replaces the whole queue. Used once, at boot, to seed the model from what was read off disk:
        /// the restored backlog <em>is</em> the queue, not an addition to it.
        /// </summary>
        internal void ReplaceAll(IEnumerable<PendingScoreEntry> entries)
        {
            _entries.Clear();
            if (entries == null)
            {
                return;
            }

            _entries.AddRange(entries);
        }
    }
}
