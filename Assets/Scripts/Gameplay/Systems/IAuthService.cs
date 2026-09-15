using System.Threading;
using Cysharp.Threading.Tasks;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Seam between the game and whatever backend identifies the player. Deliberately free of any
    /// Unity Gaming Services type so everything downstream of a signed-in player is testable without
    /// a live backend, exactly like <see cref="IRewardSource"/> keeps the ad SDK out of the power-up
    /// economy.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>True once a session exists and <see cref="PlayerId"/> is readable.</summary>
        bool IsSignedIn { get; }

        /// <summary>
        /// Backend identifier for the signed-in player, or an empty string while signed out. Empty
        /// rather than null so callers never have to null-check a string.
        /// </summary>
        string PlayerId { get; }

        /// <summary>
        /// Signs the player in without credentials, initializing the backend first if needed. Safe to
        /// call when already signed in — it completes immediately rather than starting a new session.
        /// </summary>
        UniTask SignInAnonymouslyAsync(CancellationToken cancellationToken);
    }
}
