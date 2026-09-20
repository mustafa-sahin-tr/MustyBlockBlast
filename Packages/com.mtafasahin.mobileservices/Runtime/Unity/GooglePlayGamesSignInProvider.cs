using System;
using System.Threading;
using Cysharp.Threading.Tasks;

#if MUSTY_GOOGLE_PLAY_GAMES && UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

namespace Mtafasahin.MobileServices
{
    /// <summary>
    /// Android counterpart to <see cref="AppleSignInProvider"/>: runs the Google Play Games sign-in and
    /// hands back the server auth code
    /// <see cref="Mtafasahin.MobileServices.IAuthService.LinkWithGooglePlayGamesAsync"/> needs.
    /// <para>
    /// Compiled in only when both <c>MUSTY_GOOGLE_PLAY_GAMES</c> is defined — the developer sets it
    /// after importing the Google Play Games plugin, which is not a UPM package — and the build target
    /// is Android. Every other configuration keeps the fallback below so the game always compiles.
    /// </para>
    /// <para>
    /// Note the two-step shape the plugin imposes: authenticating gets the player into Play Games, and
    /// a *second* call is what mints the one-shot auth code the backend can redeem. Skipping the second
    /// step yields a signed-in player and nothing to link with.
    /// </para>
    /// </summary>
    public sealed class GooglePlayGamesSignInProvider
    {
        private const string UNSUPPORTED_MESSAGE =
            "Google Play Games sign-in is only available on Android builds with the Play Games plugin " +
            "imported and the MUSTY_GOOGLE_PLAY_GAMES scripting define set.";

        /// <summary>
        /// Signs the player into Play Games if needed and resolves with a fresh server auth code.
        /// <para>
        /// Throws <see cref="NotSupportedException"/> where Play Games does not exist and
        /// <see cref="InvalidOperationException"/> when sign-in was refused or no code came back — the
        /// same split, for the same reason, as <see cref="AppleSignInProvider"/>.
        /// </para>
        /// </summary>
        public async UniTask<string> GetAuthCodeAsync(CancellationToken cancellationToken)
        {
#if MUSTY_GOOGLE_PLAY_GAMES && UNITY_ANDROID
            cancellationToken.ThrowIfCancellationRequested();

            // Idempotent, and cheap when already done: the plugin installs itself as the active social
            // platform, and doing it here keeps the whole Play Games dependency inside this one class.
            PlayGamesPlatform.Activate();

            bool isAuthenticated = false;
            bool isSignInComplete = false;

            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                isAuthenticated = status == SignInStatus.Success;
                isSignInComplete = true;
            });

            await UniTask.WaitUntil(() => isSignInComplete, cancellationToken: cancellationToken);

            if (!isAuthenticated)
            {
                throw new InvalidOperationException("Google Play Games sign-in was refused or failed.");
            }

            string authCode = null;
            bool isCodeComplete = false;

            // forceRefreshToken: false — a cached code is exactly as redeemable as a fresh one, and
            // forcing a refresh can put a second consent prompt in front of a player who already agreed.
            PlayGamesPlatform.Instance.RequestServerSideAccess(false, code =>
            {
                authCode = code;
                isCodeComplete = true;
            });

            await UniTask.WaitUntil(() => isCodeComplete, cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(authCode))
            {
                throw new InvalidOperationException("Google Play Games returned no server auth code.");
            }

            return authCode;
#else
            // Fallback branch, deliberately present on every other platform and configuration.
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.CompletedTask;
            throw new NotSupportedException(UNSUPPORTED_MESSAGE);
#endif
        }
    }
}
