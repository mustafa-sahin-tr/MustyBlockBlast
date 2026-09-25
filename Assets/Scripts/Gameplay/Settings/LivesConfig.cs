using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// The Path-mode lives economy's three numbers (issue #477): how many lives the hourly refill tops
    /// up to, how many each refill grants, and how many a new install starts with. Static config, so
    /// it lives in a ScriptableObject for the reason <see cref="CurrencyConfig"/> does: retuning it is
    /// an asset edit, never a code change.
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
    }
}
