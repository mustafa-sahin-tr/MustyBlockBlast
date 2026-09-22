using MustyBlockBlast.Gameplay;
using MustyBlockBlast.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Pins the power-up price pass from issue #402. Two subjects, because a ScriptableObject has two
    /// homes for a number: the C# defaults a fresh instance is born with, and the serialized values in
    /// the shipped asset the player actually shops from. A pass that edited one and not the other
    /// would leave a scene booting on the fallback config disagreeing with the real shop, so both are
    /// asserted against the same table, and the two asserts are separate tests so the report names
    /// which half drifted.
    /// </summary>
    public class PowerUpPriceConfigTests
    {
        private const string SHIPPED_ASSET_PATH = "Assets/Settings/PowerUpPriceConfig.asset";

        /// <summary>The Coin Sower unit price this issue explicitly leaves alone (AC3).</summary>
        private const int COIN_SOWER_UNIT_PRICE = 60;

        private PowerUpPriceConfig _freshConfig;

        [SetUp]
        public void CreateFreshConfig()
        {
            _freshConfig = ScriptableObject.CreateInstance<PowerUpPriceConfig>();
        }

        [TearDown]
        public void DestroyFreshConfig()
        {
            if (_freshConfig != null)
            {
                Object.DestroyImmediate(_freshConfig);
            }
        }

        /// <summary>AC1/AC2 on the C# defaults: the table a fresh instance carries.</summary>
        [Test]
        [TestCase(PowerUpKind.Joker, 10)]
        [TestCase(PowerUpKind.Bomb, 50)]
        [TestCase(PowerUpKind.RowClear, 50)]
        [TestCase(PowerUpKind.ColumnClear, 50)]
        [TestCase(PowerUpKind.ColorCleanser, 75)]
        [TestCase(PowerUpKind.Rotate, 50)]
        [TestCase(PowerUpKind.Reroll, 75)]
        [TestCase(PowerUpKind.DoubleMultiplier, 200)]
        [TestCase(PowerUpKind.GhostFit, 25)]
        public void GetPrice_OnAFreshInstance_ReturnsTheIssue402Price(PowerUpKind kind, int expected)
        {
            Assert.AreEqual(expected, _freshConfig.GetPrice(kind));
        }

        /// <summary>AC3: Coin Sower's unit price is untouched by the pass.</summary>
        [Test]
        public void GetPrice_OnAFreshInstance_LeavesCoinSowerAlone()
        {
            Assert.AreEqual(COIN_SOWER_UNIT_PRICE, _freshConfig.GetPrice(PowerUpKind.CoinSower));
        }

        /// <summary>
        /// AC5: Hold still has no coin row. <see cref="PowerUpPriceConfig.GetPrice"/> answers an
        /// unpriced kind with <c>int.MaxValue</c>, which is what keeps it out of the shop.
        /// </summary>
        [Test]
        public void GetPrice_OnAFreshInstance_StillDoesNotPriceHold()
        {
            Assert.AreEqual(int.MaxValue, _freshConfig.GetPrice(PowerUpKind.Hold));
        }

#if UNITY_EDITOR
        /// <summary>
        /// AC1/AC2 on the shipped asset — the half the issue calls out as the real risk, since the
        /// serialized array does not follow the C# defaults once the asset exists on disk.
        /// </summary>
        [Test]
        [TestCase(PowerUpKind.Joker, 10)]
        [TestCase(PowerUpKind.Bomb, 50)]
        [TestCase(PowerUpKind.RowClear, 50)]
        [TestCase(PowerUpKind.ColumnClear, 50)]
        [TestCase(PowerUpKind.ColorCleanser, 75)]
        [TestCase(PowerUpKind.Rotate, 50)]
        [TestCase(PowerUpKind.Reroll, 75)]
        [TestCase(PowerUpKind.DoubleMultiplier, 200)]
        [TestCase(PowerUpKind.GhostFit, 25)]
        public void GetPrice_OnTheShippedAsset_ReturnsTheIssue402Price(PowerUpKind kind, int expected)
        {
            PowerUpPriceConfig shipped = LoadShippedConfig();

            Assert.AreEqual(expected, shipped.GetPrice(kind));
        }

        /// <summary>AC3 on the shipped asset.</summary>
        [Test]
        public void GetPrice_OnTheShippedAsset_LeavesCoinSowerAlone()
        {
            Assert.AreEqual(COIN_SOWER_UNIT_PRICE, LoadShippedConfig().GetPrice(PowerUpKind.CoinSower));
        }

        /// <summary>AC5 on the shipped asset.</summary>
        [Test]
        public void GetPrice_OnTheShippedAsset_StillDoesNotPriceHold()
        {
            Assert.AreEqual(int.MaxValue, LoadShippedConfig().GetPrice(PowerUpKind.Hold));
        }

        private static PowerUpPriceConfig LoadShippedConfig()
        {
            PowerUpPriceConfig shipped = AssetDatabase.LoadAssetAtPath<PowerUpPriceConfig>(SHIPPED_ASSET_PATH);
            Assert.IsNotNull(shipped, $"No PowerUpPriceConfig at {SHIPPED_ASSET_PATH}.");
            return shipped;
        }
#endif
    }
}
