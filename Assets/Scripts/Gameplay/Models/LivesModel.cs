using Mtafasahin.Reactive;

namespace MustyBlockBlast.Gameplay.Models
{
    /// <summary>
    /// The player's Path-mode lives (issue #477). Owned and written by
    /// <see cref="Systems.LivesSystem"/>; the HUD's lives section only reads it.
    /// </summary>
    public sealed class LivesModel
    {
        /// <summary>
        /// Lives the player holds now. Never negative. Usually at most
        /// <see cref="Settings.LivesConfig.RegenCap"/>, but not always: a coin pack may take it past
        /// the cap, and the refill never brings it back down.
        /// </summary>
        public ReactiveProperty<int> CurrentLives { get; } = new ReactiveProperty<int>(0);

        /// <summary>
        /// Whole seconds until the next xx:00 refill, 1 to 3600, while lives are below the cap — and 0
        /// while they are at or above it, when there is no refill to wait for. So 0 is the one signal a
        /// view needs to hide its countdown.
        /// </summary>
        public ReactiveProperty<int> SecondsUntilRefill { get; } = new ReactiveProperty<int>(0);
    }
}
