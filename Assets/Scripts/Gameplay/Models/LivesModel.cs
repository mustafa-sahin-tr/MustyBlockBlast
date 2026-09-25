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

        /// <summary>
        /// The count just before the last Path failure charged a life (issue #478) — so the fail card can
        /// read "17 → 16" — or 0 while the run just ended cost nothing. A charge always starts from at
        /// least one life, so 0 is never a real "before" and doubles as "nothing charged".
        /// <para>
        /// Cleared when the next run starts and when a rescue refunds the charge, so it only ever
        /// describes the ending on screen. A failure at zero lives charges nothing and leaves it at 0:
        /// that is how a view tells "this run cost a life" from "this run found none to take".
        /// </para>
        /// </summary>
        public ReactiveProperty<int> LivesBeforeLastCharge { get; } = new ReactiveProperty<int>(0);
    }
}
