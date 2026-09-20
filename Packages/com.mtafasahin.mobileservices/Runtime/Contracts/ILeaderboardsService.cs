using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Mtafasahin.MobileServices
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
        /// <para>
        /// <paramref name="metadata"/> rides along with the entry and is what makes a fetched board
        /// renderable: the player's name comes from the backend's own identity, but everything else a row
        /// draws has to be attached here — today that is
        /// <see cref="LeaderboardMetadataKeys.AVATAR_ID"/>. May be null, which files a bare score; an
        /// entry without metadata is a drawable entry, just a plainer one (see
        /// <see cref="LeaderboardEntryData.AvatarId"/>).
        /// </para>
        /// </summary>
        UniTask AddPlayerScoreAsync(
            string leaderboardId,
            int score,
            IReadOnlyDictionary<string, string> metadata,
            CancellationToken cancellationToken);

        /// <summary>
        /// Reads the top <paramref name="limit"/> entries of <paramref name="leaderboardId"/>, best
        /// first, with each entry's metadata already unpacked into
        /// <see cref="LeaderboardEntryData"/>.
        /// <para>
        /// Top-of-board only, and bounded by the caller: the one screen that reads a board draws a fixed
        /// number of rows and cannot scroll, so fetching a page the player could never see would be paid
        /// for in bandwidth and rendered nowhere. Paging and "scores around me" are deliberately absent
        /// until a screen exists that needs them.
        /// </para>
        /// <para>
        /// Throws whatever the backend throws — unlike submission, a failed read has a caller that can
        /// say so (<see cref="LeaderboardQuerySystem"/> turns it into a message on the card), so
        /// swallowing it here would only hide it.
        /// </para>
        /// </summary>
        UniTask<IReadOnlyList<LeaderboardEntryData>> GetScoresAsync(
            string leaderboardId,
            int limit,
            CancellationToken cancellationToken);
    }
}
