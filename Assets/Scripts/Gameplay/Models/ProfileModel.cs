using MustyBlockBlast.Gameplay.Reactive;
using MustyBlockBlast.Gameplay.Systems;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The player's public identity: what other players see next to a score. Owned and persisted by
    /// <see cref="ProfileSystem"/>.
    /// <para>
    /// Separate from <see cref="SettingsModel"/> even though both are "player preferences that survive
    /// a run": settings are private device choices, this is published to a backend and to other
    /// players, and only this half needs a round trip to a server before a change can be trusted.
    /// </para>
    /// <para>
    /// <see cref="AvatarId"/> is an index into the avatar set rather than a sprite reference, because a
    /// Model may not hold an engine type — and because the id is what a later leaderboard submission
    /// will put in its entry metadata.
    /// </para>
    /// </summary>
    public sealed class ProfileModel
    {
        /// <summary>
        /// How many preset avatars exist. The one place the count is stated: <see cref="ProfileSystem"/>
        /// clamps a saved id against it and the profile card sizes its picker grid from it, so the two
        /// can never disagree about which ids are legal.
        /// </summary>
        public const int AVATAR_COUNT = 12;

        /// <summary>Empty until the player picks one. Empty rather than a generated placeholder so a
        /// View can tell "never set" from "deliberately set" and prompt accordingly.</summary>
        public ReactiveProperty<string> DisplayName { get; } = new ReactiveProperty<string>(string.Empty);

        /// <summary>Index into the preset avatar set. Defaults to the first preset so the player always
        /// has a picture, including before they ever open the profile card.</summary>
        public ReactiveProperty<int> AvatarId { get; } = new ReactiveProperty<int>(0);

        public ReactiveProperty<AccountLinkStatus> LinkStatus { get; } =
            new ReactiveProperty<AccountLinkStatus>(AccountLinkStatus.Anonymous);
    }
}
