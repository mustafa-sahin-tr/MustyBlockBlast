using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The Path-mode lives economy's numbers (issue #477): how many lives the hourly refill tops up to,
    /// how many each refill grants, how many a new install starts with — and, since issue #478, how
    /// many one rewarded ad on the out-of-lives sheet pays; and, since issue #479, what the sheet's coin
    /// lives pack grants and costs. Static config, so it lives in a ScriptableObject for the reason
    /// <see cref="CurrencyConfig"/> does: retuning it is an asset edit, never a code change.
    /// <para>
    /// The cap bounds the refill only. Coin packs (issue #479) are allowed to take the count past it,
    /// and nothing here — or in <see cref="Systems.LivesSystem"/> — ever lowers a count that is already
    /// above it.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Lives Config", fileName = "LivesConfig")]
    public sealed class LivesConfig : ScriptableObject
    {
        [Tooltip("The hourly refill tops lives up to this many and no further. A count already at or "
            + "above it (e.g. after a coin pack) is left alone.")]
        [SerializeField] private int _regenCap = 20;

        [Tooltip("Lives granted at every wall-clock hour boundary (xx:00) crossed, clamped at the cap.")]
        [SerializeField] private int _refillAmount = 5;

        [Tooltip("Lives a device with no saved lives starts with — new and existing installs alike.")]
        [SerializeField] private int _startingLives = 20;

        [Tooltip("Lives one rewarded ad on the out-of-lives sheet grants, clamped at the cap. The ad is "
            + "not offered at all while lives are at or above the cap.")]
        [SerializeField] private int _adRewardAmount = 3;

        [Tooltip("Lives the out-of-lives sheet's coin lives pack grants. NOT clamped at the cap — a pack "
            + "bought at 22 lives leaves 32.")]
        [SerializeField] private int _livesPackAmount = 10;

        [Tooltip("Coins the lives pack costs. PLACEHOLDER — the price is not decided yet (issue #479); tune "
            + "it here once the economy settles.")]
        [SerializeField] private int _livesPackCoinPrice = 150;

        /// <summary>
        /// What the hourly refill tops up to. At least 1: a zero cap would make the refill a no-op
        /// forever, which is spelled by setting the refill amount to zero, not by an unreachable cap.
        /// </summary>
        public int RegenCap => Mathf.Max(1, _regenCap);

        /// <summary>Lives per hour boundary crossed. Never negative — a negative refill would turn the
        /// hourly top-up into an hourly fine.</summary>
        public int RefillAmount => Mathf.Max(0, _refillAmount);

        /// <summary>Lives a first launch starts with. Never negative, for the reason the refill is not.</summary>
        public int StartingLives => Mathf.Max(0, _startingLives);

        /// <summary>Lives one watched ad asks for (issue #478). At least 1: an ad that pays nothing is not
        /// an offer, and the sheet would be showing a button that does nothing.</summary>
        public int AdRewardAmount => Mathf.Max(1, _adRewardAmount);

        /// <summary>Lives one coin lives pack grants (issue #479), uncapped. At least 1, for the reason the
        /// ad's amount is: a pack that grants nothing would be a sheet button that only takes coins.</summary>
        public int LivesPackAmount => Mathf.Max(1, _livesPackAmount);

        /// <summary>
        /// What one lives pack costs in coins (issue #479). A placeholder figure until the economy is
        /// tuned. At least 1: a free pack would be an unlimited, uncapped lives faucet — the one thing the
        /// cap on every other lives source exists to prevent.
        /// </summary>
        public int LivesPackCoinPrice => Mathf.Max(1, _livesPackCoinPrice);
    }
}
