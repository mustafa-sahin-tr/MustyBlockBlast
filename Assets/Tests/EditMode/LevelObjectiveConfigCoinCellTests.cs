using MustyBlockBlast.Gameplay.Settings;
using NUnit.Framework;
using UnityEngine;

namespace MustyBlockBlast.Tests.EditMode
{
    /// <summary>
    /// Covers the authored coin-cell count: absent means none, a negative is clamped rather than
    /// trusted, and a real figure survives validation untouched.
    /// <para>
    /// Rows are authored through <see cref="JsonUtility"/> rather than reflection, exactly as
    /// <c>LevelProgressionSystemPathModeTests</c> authors its catalog: the serialized field names are the
    /// asset's own contract, so a row written this way is what the Inspector would have produced.
    /// </para>
    /// </summary>
    public class LevelObjectiveConfigCoinCellTests
    {
        /// <summary>The compatibility claim: every level authored before this field existed has no
        /// <c>_coinCellCount</c> in its serialized data at all, and must seed nothing.</summary>
        [Test]
        public void CoinCellCount_OnARowAuthoredBeforeTheFieldExisted_IsZero()
        {
            LevelObjectiveConfig config = ARow("{\"_levelNumber\":1,\"_targetValue\":1}");

            Assert.AreEqual(0, config.CoinCellCount);
        }

        [Test]
        public void CoinCellCount_OnARowAuthoringThree_IsThree()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_coinCellCount\":3}");

            Assert.AreEqual(3, config.CoinCellCount);
        }

        /// <summary>A negative count is nonsense the Inspector can produce by a typo, and it is clamped
        /// where every other numeric field is rather than at the one place that reads it.</summary>
        [TestCase(-1)]
        [TestCase(-50)]
        public void ValidateInEditor_WithANegativeCoinCellCount_ClampsItToZero(int authored)
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_targetValue\":1,\"_coinCellCount\":{authored}}}");

            config.ValidateInEditor();

            Assert.AreEqual(0, config.CoinCellCount);
        }

        [Test]
        public void ValidateInEditor_WithAPositiveCoinCellCount_LeavesItAlone()
        {
            LevelObjectiveConfig config = ARow(
                "{\"_levelNumber\":1,\"_targetValue\":1,\"_coinCellCount\":4}");

            config.ValidateInEditor();

            Assert.AreEqual(4, config.CoinCellCount);
        }

        /// <summary>A coin-cell count is board dressing, not an objective: it can never make a row
        /// unplayable, however it is authored.</summary>
        [TestCase(-5)]
        [TestCase(0)]
        [TestCase(7)]
        public void IsValid_IsUnaffectedByTheCoinCellCount(int authored)
        {
            LevelObjectiveConfig config = ARow(
                $"{{\"_levelNumber\":1,\"_targetValue\":1,\"_requiredLineCount\":1,"
                + $"\"_coinCellCount\":{authored}}}");

            Assert.IsTrue(config.IsValid(out string error), error);
        }

        private static LevelObjectiveConfig ARow(string json)
            => JsonUtility.FromJson<LevelObjectiveConfig>(json);
    }
}
