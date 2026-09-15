using System.Collections.Generic;
using MustyBlockBlast.Gameplay.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// State that belongs to a walk along the level path: which level the Path-mode run in progress is
    /// playing, and the running total of what every level of that walk has scored.
    /// <para>
    /// Deliberately separate from <see cref="ScoreModel"/>. Everything on that model resets on
    /// <c>RunStartedMessage</c> — that is its whole shape, and readers rely on it — whereas a path
    /// total exists precisely to survive the runs it sums. Bolting an exception onto ScoreModel would
    /// cost that invariant for one field.
    /// </para>
    /// <para>
    /// Equally separate from <see cref="LevelProgressionModel"/>, which is the <em>linear frontier</em>:
    /// the highest level the player has unlocked, persisted, and only ever moved forward by a first
    /// clear. <see cref="ActiveLevelNumber"/> is "the level this run happens to be playing", which in
    /// Path mode can be any already-unlocked level the player tapped. Keeping the two apart is what
    /// stops replaying a cleared level from rewriting the frontier.
    /// </para>
    /// <para>
    /// Not persisted: a path total is a session tally of the walk in progress, like a run score.
    /// </para>
    /// </summary>
    public sealed class PathRunModel
    {
        /// <summary><see cref="ActiveLevelNumber"/> while no Path-mode run is active — level numbers are 1-based.</summary>
        public const int NO_ACTIVE_LEVEL = 0;

        /// <summary>
        /// The level the Path-mode run in progress is playing, or <see cref="NO_ACTIVE_LEVEL"/> outside
        /// Path mode.
        /// </summary>
        public ReactiveProperty<int> ActiveLevelNumber { get; } =
            new ReactiveProperty<int>(NO_ACTIVE_LEVEL);

        /// <summary>
        /// Sum of each distinct level's BEST final score on the current walk (base + completion bonus),
        /// not a running total of every completion event. A level replayed several times contributes
        /// its best result once — the total goes up only when that replay beats the level's previous
        /// best on this walk, never merely for playing it again.
        /// </summary>
        public ReactiveProperty<int> PathTotalScore { get; } = new ReactiveProperty<int>(0);

        /// <summary>The best final score banked so far this walk, per level. Never persisted — cleared
        /// with the rest of the walk's state by <see cref="ResetWalk"/>.</summary>
        private readonly Dictionary<int, int> _bestScoreByLevel = new Dictionary<int, int>();

        /// <summary>Discards the walk's tally: an empty best-score table and a zero total. Called at
        /// every edge a walk starts or ends — entering Path mode, restarting from level 1, and leaving
        /// Path mode. Deliberately leaves <see cref="ActiveLevelNumber"/> alone: which level is being
        /// played is set by the caller in the same breath (to the started level, or to
        /// <see cref="NO_ACTIVE_LEVEL"/> on the way out), and clearing it here would only fight that.</summary>
        internal void ResetWalk()
        {
            _bestScoreByLevel.Clear();
            PathTotalScore.Value = 0;
        }

        /// <summary>
        /// Banks a level completion against this walk's total. Folds <paramref name="finalScore"/> into
        /// <see cref="PathTotalScore"/> only by the amount it improves on that level's previous best this
        /// walk (zero if it is a repeat or a worse replay) — see <see cref="PathTotalScore"/> for why.
        /// </summary>
        internal void RecordLevelCompletion(int levelNumber, int finalScore)
        {
            int previousBest = _bestScoreByLevel.TryGetValue(levelNumber, out int existing) ? existing : 0;
            if (finalScore <= previousBest)
            {
                return;
            }

            _bestScoreByLevel[levelNumber] = finalScore;
            PathTotalScore.Value += finalScore - previousBest;
        }
    }
}
