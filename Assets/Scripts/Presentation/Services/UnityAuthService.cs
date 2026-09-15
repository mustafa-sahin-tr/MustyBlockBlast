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
    }
}
