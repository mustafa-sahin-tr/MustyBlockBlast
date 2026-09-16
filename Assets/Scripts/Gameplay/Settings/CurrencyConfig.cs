using UnityEngine;

namespace MustyBlockBlast.Gameplay.Settings
{
    /// <summary>
    /// How much a point is worth in coins. Static config, so it lives in a ScriptableObject for the
    /// reason <see cref="TimedModeConfig"/> does: retuning the economy is an asset edit, never a code
    /// change — and this is one number the game will certainly be retuned on.
    /// <para>
    /// A placeholder rate for now. Nothing spends coins yet (that is a later slice), so there is no
    /// sink to balance the faucet against; what matters here is that the rate has exactly one home.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "MustyBlockBlast/Currency Config", fileName = "CurrencyConfig")]
    public sealed class CurrencyConfig : ScriptableObject
    {
        [Header("Conversion")]
        [Tooltip("Coins earned per point of converted score. 0.1 means 100 points buy 10 coins.")]
        [SerializeField] private float _scoreToCoinRate = 0.1f;

        [Tooltip("Coins one rewarded ad grants. Separate from the conversion rate — an ad grant is not a conversion.")]
        [SerializeField] private int _adRewardCoins = 25;

        /// <summary>
        /// Coins per point of converted score. Never negative: a negative rate would make conversion a
        /// way to lose coins, and the clamp is here rather than at the call site so every reader gets it.
        /// </summary>
        public float ScoreToCoinRate => Mathf.Max(0f, _scoreToCoinRate);

        /// <summary>Coins one rewarded ad is worth. Clamped for the reason the rate is.</summary>
        public int AdRewardCoins => Mathf.Max(0, _adRewardCoins);
    }
}
