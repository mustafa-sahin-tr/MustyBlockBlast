using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Seam between the game and whatever backend stores ranked scores. Deliberately free of any Unity
    /// Gaming Services type so <see cref="LeaderboardSystem"/> — the part that decides which board a run
    /// belongs on and whether it may be submitted at all — is testable without a live backend, exactly
    /// like <see cref="IAuthService"/> keeps the SDK out of everything downstream of a signed-in player.
    /// </summary>
    public interface ILeaderboardsService
    {
        /// <summary>
        /// Files <paramref name="score"/> against the board named by <paramref name="leaderboardId"/>.
        /// Whether that score replaces the player's existing entry is a server-side decision (the boards
        /// are configured "Keep Best"), so callers submit unconditionally and never compare locally.
        /// <para>
        /// Takes an <see cref="int"/> rather than the backend's wider numeric type because that is what a
        /// run's score actually is (<c>ScoreModel.Score</c>); widening it is the implementation's job, not
        /// every caller's.
        /// </para>
        /// </summary>
        UniTask AddPlayerScoreAsync(string leaderboardId, int score, CancellationToken cancellationToken);
    }
}
