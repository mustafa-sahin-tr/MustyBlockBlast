using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Systems;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Unity Gaming Services implementation of <see cref="IAuthService"/>. The only type in the project
    /// that touches the UGS SDK, so swapping backends — or stubbing one out in a test — is one binding
    /// in the LifetimeScope.
    /// </summary>
    public sealed class UnityAuthService : IAuthService
    {
        // Provider names travel out on AccountAlreadyLinkedException, so they are fixed here rather than
        // spelled inline at each throw site where a typo would reach the player as a broken message.
        private const string APPLE_PROVIDER = "Apple";
        private const string GOOGLE_PLAY_GAMES_PROVIDER = "GooglePlayGames";

        // AuthenticationService.Instance does not exist until core services are initialized, so every
        // read is gated on the initialization state rather than on the auth service alone.
        public bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance != null &&
            AuthenticationService.Instance.IsSignedIn;

        // Coalesced rather than returned straight through: IAuthService promises callers a non-null
        // string, and that promise should not rest on an SDK property's undocumented nullability.
        public string PlayerId =>
            IsSignedIn ? AuthenticationService.Instance.PlayerId ?? string.Empty : string.Empty;

        public async UniTask SignInAnonymouslyAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Initializing twice throws, and the SDK is a process-wide static: a second scene — or a
            // second scope in the same session — finds services already up and must skip straight to
            // the sign-in below.
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // AttachExternalCancellation rather than a cancellable overload because the SDK call
                // itself takes no token: cancelling abandons the await (so nothing touches a disposed
                // scope), while the underlying request is left to finish on its own.
                await UnityServices.InitializeAsync()
                    .AsUniTask()
                    .AttachExternalCancellation(cancellationToken);
            }

            // An anonymous session is cached on device and restored by InitializeAsync, so on every
            // launch after the first the player is already signed in and a second sign-in would be
            // both wasteful and an SDK error.
            if (AuthenticationService.Instance.IsSignedIn)
            {
                return;
            }

            await AuthenticationService.Instance.SignInAnonymouslyAsync()
                .AsUniTask()
                .AttachExternalCancellation(cancellationToken);
        }

        public async UniTask LinkWithAppleAsync(string identityToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await AuthenticationService.Instance.LinkWithAppleAsync(identityToken)
                    .AsUniTask()
                    .AttachExternalCancellation(cancellationToken);
            }
            catch (AuthenticationException exception)
                when (exception.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                // Translated at the seam so callers never have to know the SDK's error-code table. Every
                // other failure — expired token, no network, malformed request — is left alone on
                // purpose: they all mean "try again later", which is the caller's generic error path.
                throw new AccountAlreadyLinkedException(APPLE_PROVIDER, exception);
            }
        }

        public async UniTask LinkWithGooglePlayGamesAsync(string authCode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await AuthenticationService.Instance.LinkWithGooglePlayGamesAsync(authCode)
                    .AsUniTask()
                    .AttachExternalCancellation(cancellationToken);
            }
            catch (AuthenticationException exception)
                when (exception.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                throw new AccountAlreadyLinkedException(GOOGLE_PLAY_GAMES_PROVIDER, exception);
            }
        }
    }
}
