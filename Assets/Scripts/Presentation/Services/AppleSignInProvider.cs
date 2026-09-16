using System;
using System.Threading;
using Cysharp.Threading.Tasks;

#if MUSTY_APPLE_SIGNIN && UNITY_IOS
using System.Text;
using AppleAuth;
using AppleAuth.Enums;
using AppleAuth.Interfaces;
using AppleAuth.Native;
#endif

namespace MustyBlockBlast.Presentation.Services
{
    /// <summary>
    /// Runs the native Sign in with Apple prompt and hands back the identity token
    /// <see cref="MustyBlockBlast.Gameplay.Systems.IAuthService.LinkWithAppleAsync"/> needs. Sits on the
    /// Presentation side of the auth seam for the same reason <see cref="UnityAuthService"/> does:
    /// acquiring the credential is a platform concern, and nothing downstream of a linked account should
    /// have to know a native plugin exists.
    /// <para>
    /// Compiled in only when both <c>MUSTY_APPLE_SIGNIN</c> is defined — the developer sets it after
    /// importing the Apple sign-in plugin, which is not a UPM package and so cannot be a project
    /// dependency — and the build target is iOS. Every other configuration keeps the fallback below, so
    /// the game always compiles and an unsupported platform fails with a sentence a caller can show
    /// rather than with a missing-type error at build time.
    /// </para>
    /// </summary>
    public sealed class AppleSignInProvider
    {
        private const string UNSUPPORTED_MESSAGE =
            "Sign in with Apple is only available on iOS builds with the Apple sign-in plugin imported " +
            "and the MUSTY_APPLE_SIGNIN scripting define set.";

#if MUSTY_APPLE_SIGNIN && UNITY_IOS
        /// <summary>
        /// Created on first use rather than in the constructor: constructing the manager touches the
        /// native library, and the container builds this object at scene load whether or not the player
        /// ever opens the profile card.
        /// </summary>
        private IAppleAuthManager _appleAuthManager;
#endif

        /// <summary>
        /// Shows the native prompt and resolves with the identity token, or throws.
        /// <para>
        /// Throws <see cref="NotSupportedException"/> where Sign in with Apple does not exist, and
        /// <see cref="InvalidOperationException"/> when the prompt itself failed or the player dismissed
        /// it. Two types rather than one so the caller can tell "this device can never do this" — where
        /// the button should not have been offered at all — from "that did not work, try again".
        /// </para>
        /// </summary>
        public async UniTask<string> GetIdentityTokenAsync(CancellationToken cancellationToken)
        {
#if MUSTY_APPLE_SIGNIN && UNITY_IOS
            cancellationToken.ThrowIfCancellationRequested();

            if (!AppleAuthManager.IsCurrentPlatformSupported)
            {
                throw new NotSupportedException(UNSUPPORTED_MESSAGE);
            }

            if (_appleAuthManager == null)
            {
                _appleAuthManager = new AppleAuthManager(new PayloadDeserializer());
            }

            var loginArgs = new AppleAuthLoginArgs(LoginOptions.None);

            string identityToken = null;
            string failureMessage = null;
            bool isComplete = false;

            _appleAuthManager.LoginWithAppleId(
                loginArgs,
                credential =>
                {
                    var appleIdCredential = credential as IAppleIDCredential;
                    if (appleIdCredential == null || appleIdCredential.IdentityToken == null)
                    {
                        failureMessage = "Apple returned a credential without an identity token.";
                    }
                    else
                    {
                        identityToken = Encoding.UTF8.GetString(
                            appleIdCredential.IdentityToken, 0, appleIdCredential.IdentityToken.Length);
                    }

                    isComplete = true;
                },
                error =>
                {
                    failureMessage = "Sign in with Apple failed: " + error.LocalizedDescription;
                    isComplete = true;
                });

            // The plugin delivers its callbacks only while Update is pumped, so the wait has to drive
            // that pump rather than simply idle. Cancellation abandons the wait; the native prompt is
            // left to finish on its own, exactly as the UGS calls behind IAuthService are.
            while (!isComplete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _appleAuthManager.Update();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (identityToken == null)
            {
                throw new InvalidOperationException(failureMessage ?? "Sign in with Apple failed.");
            }

            return identityToken;
#else
            // Fallback branch, deliberately present on every other platform and configuration: silently
            // compiling to nothing would leave the caller awaiting a token that never arrives.
            cancellationToken.ThrowIfCancellationRequested();
            await UniTask.CompletedTask;
            throw new NotSupportedException(UNSUPPORTED_MESSAGE);
#endif
        }
    }
}
