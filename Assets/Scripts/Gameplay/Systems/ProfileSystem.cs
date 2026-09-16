using System.Threading;
using Cysharp.Threading.Tasks;
using MustyBlockBlast.Gameplay.Models;
using UnityEngine;

namespace MustyBlockBlast.Gameplay.Systems
{
    /// <summary>
    /// Owns <see cref="ProfileModel"/>: the player's display name, avatar and how durable their account
    /// is. Every mutation of that identity goes through here.
    /// <para>
    /// Both the name and the avatar are written to PlayerPrefs, one flat key each, exactly as
    /// <see cref="BadgeStatsSystem"/> writes its counters — they move independently and share no
    /// invariant, so there is nothing to write atomically. The local copy is the one the game renders:
    /// the backend is told the name so other players can see it, but a dropped connection must not cost
    /// the player the name they just chose on their own device.
    /// </para>
    /// <para>
    /// The avatar is deliberately local-only for now. Nothing submits it yet; persisting it here is what
    /// lets a later slice attach it to a leaderboard entry without asking the player to choose again.
    /// </para>
    /// </summary>
    public sealed class ProfileSystem
    {
        private const string DISPLAY_NAME_KEY = "Profile.DisplayName";
        private const string AVATAR_ID_KEY = "Profile.AvatarId";

        /// <summary>
        /// Backend limit on a player name. Checked here rather than left to the server so an over-long
        /// name fails instantly and locally instead of costing a round trip.
        /// </summary>
        private const int MAX_NAME_LENGTH = 50;

        private const int MIN_NAME_LENGTH = 2;

        private readonly ProfileModel _model;
        private readonly IAuthService _authService;

        public ProfileSystem(ProfileModel model, IAuthService authService)
        {
            _model = model;
            _authService = authService;

            Load();

            // Read once at construction rather than subscribed to: a link can only happen through this
            // system, so every later change passes through LinkAppleAsync/LinkGooglePlayGamesAsync and
            // updates the model there.
            _model.LinkStatus.Value = _authService.GetLinkStatus();
        }

        /// <summary>
        /// Validates, publishes and persists a new display name.
        /// <para>
        /// Returns <c>false</c> — without contacting the backend — when the name is empty, the wrong
        /// length, carries characters the backend forbids, or trips <see cref="ProfanityFilter"/>. The
        /// model is left untouched in that case, so a rejected name never appears anywhere.
        /// </para>
        /// <para>
        /// Backend failures are not swallowed into that same <c>false</c>: they surface as the
        /// exception the auth service threw, because "that name is not allowed" and "we could not reach
        /// the server" need different words and a different offer to the player.
        /// </para>
        /// </summary>
        public async UniTask<bool> UpdateDisplayNameAsync(string name, CancellationToken cancellationToken)
        {
            string trimmed = name == null ? string.Empty : name.Trim();

            if (!IsWellFormed(trimmed) || ProfanityFilter.ContainsProfanity(trimmed))
            {
                return false;
            }

            await _authService.UpdatePlayerNameAsync(trimmed, cancellationToken);

            // Persisted only after the backend accepted it, so the saved name and the published one
            // cannot drift apart.
            PlayerPrefs.SetString(DISPLAY_NAME_KEY, trimmed);
            _model.DisplayName.Value = trimmed;
            return true;
        }

        /// <summary>
        /// Picks a preset avatar. Purely local and instant — there is no backend call to fail, which is
        /// why this is not async while the name change is.
        /// </summary>
        public void SetAvatarId(int avatarId)
        {
            if (avatarId < 0 || avatarId >= ProfileModel.AVATAR_COUNT || avatarId == _model.AvatarId.Value)
            {
                return;
            }

            PlayerPrefs.SetInt(AVATAR_ID_KEY, avatarId);
            _model.AvatarId.Value = avatarId;
        }

        /// <summary>
        /// Attaches an Apple identity to this player. <see cref="AccountAlreadyLinkedException"/> is
        /// allowed through: only the caller knows how to ask the player whether they meant to switch
        /// accounts, and swallowing it here would leave the card reporting success over a link that
        /// never happened.
        /// </summary>
        public async UniTask LinkAppleAsync(string identityToken, CancellationToken cancellationToken)
        {
            await _authService.LinkWithAppleAsync(identityToken, cancellationToken);

            // Re-read rather than assumed: the service is the authority on what the session now holds,
            // and asking it keeps this from claiming a link the backend did not actually record.
            _model.LinkStatus.Value = _authService.GetLinkStatus();
        }

        /// <summary>Android counterpart to <see cref="LinkAppleAsync"/>.</summary>
        public async UniTask LinkGooglePlayGamesAsync(string authCode, CancellationToken cancellationToken)
        {
            await _authService.LinkWithGooglePlayGamesAsync(authCode, cancellationToken);
            _model.LinkStatus.Value = _authService.GetLinkStatus();
        }

        /// <summary>
        /// The character rules the backend enforces on a player name: letters, digits, dot, dash and
        /// underscore only. Mirrored locally so a name that cannot possibly be accepted is rejected
        /// before it costs a round trip.
        /// </summary>
        private static bool IsWellFormed(string name)
        {
            if (name.Length < MIN_NAME_LENGTH || name.Length > MAX_NAME_LENGTH)
            {
                return false;
            }

            for (int charIndex = 0; charIndex < name.Length; charIndex++)
            {
                char character = name[charIndex];
                bool isAllowed = char.IsLetterOrDigit(character)
                    || character == '.'
                    || character == '-'
                    || character == '_';

                if (!isAllowed)
                {
                    return false;
                }
            }

            return true;
        }

        private void Load()
        {
            _model.DisplayName.Value = PlayerPrefs.GetString(DISPLAY_NAME_KEY, string.Empty);

            // Clamped rather than trusted: a saved id from a build with more avatars than this one would
            // otherwise index past the picker grid every time the card opens.
            int savedAvatarId = PlayerPrefs.GetInt(AVATAR_ID_KEY, 0);
            _model.AvatarId.Value = savedAvatarId >= 0 && savedAvatarId < ProfileModel.AVATAR_COUNT
                ? savedAvatarId
                : 0;
        }
    }
}
