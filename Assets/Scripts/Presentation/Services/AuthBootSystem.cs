using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Systems;
using VContainer.Unity;
using Debug = UnityEngine.Debug;

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Signs the player in as soon as a scene boots. Pure C# — no MonoBehaviour, no View — and
    /// registered as a VContainer entry point, like <see cref="SplashSystem"/>.
    /// <para>
    /// Nothing waits on this: VContainer starts every <see cref="IAsyncStartable"/> side by side, so
    /// sign-in overlaps the splash rather than delaying it. A failure here is logged and otherwise
    /// ignored — the game is fully playable offline, and only the leaderboard needs an identity.
    /// </para>
    /// </summary>
    public sealed class AuthBootSystem : IAsyncStartable
    {
        private readonly IAuthService _authService;

        public AuthBootSystem(IAuthService authService)
        {
            _authService = authService;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                // SuppressCancellationThrow: the splash scene unloads a couple of seconds in and
                // cancels this token as ordinary teardown, which is not an error worth logging.
                bool canceled = await _authService.SignInAnonymouslyAsync(cancellation)
                    .SuppressCancellationThrow();

                if (canceled)
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                // Anything else is a genuine backend problem — an unlinked project, no network, a
                // service outage. Report it and let the game carry on without an identity.
                Debug.LogError($"Anonymous sign-in failed: {exception.Message}");
                return;
            }

            LogSignedIn(_authService.PlayerId);
        }

        /// <summary>
        /// Stripped from release players: the player id is a boot-time diagnostic, and the project
        /// keeps unconditional logging for genuine errors only. Stacked <see cref="ConditionalAttribute"/>s
        /// are OR'd, so the call survives if either symbol is defined and is compiled out when neither
        /// is. DEBUG rather than the deprecated DEVELOPMENT_BUILD (warning UAC0009); Unity defines it
        /// in the Editor and in development players, and not in a release player.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEBUG")]
        private static void LogSignedIn(string playerId)
        {
            Debug.Log($"Signed in anonymously. Player id: {playerId}");
        }
    }
}
