using System.Collections.Generic;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// The one place that knows which boards a mode ranks on. Lives outside
    /// <see cref="LeaderboardSystem"/> because <see cref="PendingScoreQueueSystem"/> needs the same
    /// mapping when it flushes a score that was banked offline: two copies could drift, and a drifted
    /// copy would file a queued score against a board the live path never uses.
    /// </summary>
    internal static class LeaderboardBoardIds
    {
        /// <summary>
        /// Board ids per mode, exactly as configured on the UGS Dashboard. Absence is meaningful:
        /// <see cref="GameMode.Path"/> has no entry because a path run is bound to one authored level, so
        /// its score measures the level rather than the player and ranking it against other players'
        /// runs would compare unlike things. Any mode added later is likewise a no-op until it is
        /// deliberately given boards here.
        /// </summary>
        private static readonly Dictionary<GameMode, (string AllTimeId, string WeeklyId)> Boards =
            new Dictionary<GameMode, (string AllTimeId, string WeeklyId)>
            {
                { GameMode.Endless, ("endless_all_time", "endless_weekly") },
                { GameMode.Timed, ("timed_all_time", "timed_weekly") },
            };

        /// <summary>
        /// False when <paramref name="mode"/> does not rank. Callers treat that as "nothing to submit"
        /// rather than an error — see the remark on <see cref="Boards"/>.
        /// </summary>
        internal static bool TryGetBoards(GameMode mode, out (string AllTimeId, string WeeklyId) boards)
        {
            return Boards.TryGetValue(mode, out boards);
        }
    }
}
