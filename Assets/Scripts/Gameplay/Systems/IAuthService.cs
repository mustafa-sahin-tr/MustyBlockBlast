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

        /// <summary>
        /// Attaches an Apple identity to the current session so the player's progress survives a
        /// reinstall or follows them to another device. The token is passed in rather than fetched here
        /// because acquiring it needs a native iOS Sign in with Apple prompt, which is a View concern —
        /// this seam only forwards the result. Throws
        /// <see cref="AccountAlreadyLinkedException"/> when that Apple ID already owns a different
        /// backend identity, so the caller can offer to switch accounts instead of retrying.
        /// </summary>
        UniTask LinkWithAppleAsync(string identityToken, CancellationToken cancellationToken);

        /// <summary>
        /// Android counterpart to <see cref="LinkWithAppleAsync"/>, taking the server auth code that
        /// Google Play Games hands back after its native sign-in. Throws
        /// <see cref="AccountAlreadyLinkedException"/> when that Play Games account already owns a
        /// different backend identity.
        /// </summary>
        UniTask LinkWithGooglePlayGamesAsync(string authCode, CancellationToken cancellationToken);
    }
}
